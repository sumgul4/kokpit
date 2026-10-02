using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitDataService.cs — ALL database access of the cockpit lives in this file.
//    KokpitServiceCollectionExtensions  one-line registration for Program.cs
//    KokpitSnapshot                     in-memory copy of the nine datasets, shared by all users
//    KokpitDataService                  loads the snapshot in one round trip (Singleton)
//    KokpitRefreshWorker                background job that refreshes it
//    KokpitSqlConnectionFactory         connection to the reporting database
//  Pages never query the database: they read KokpitDataService.Current.
// =====================================================================================================

public static class KokpitServiceCollectionExtensions
{
    /// <summary>Program.cs: <c>builder.Services.AddKokpit(builder.Configuration);</c></summary>
    public static IServiceCollection AddKokpit(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KokpitOptions>(configuration.GetSection(KokpitOptions.SectionName));
        services.AddSingleton<IKokpitConnectionFactory, KokpitSqlConnectionFactory>();
        services.AddSingleton<KokpitDataService>();
        services.AddHostedService<KokpitRefreshWorker>();
        services.AddScoped<KokpitFilterState>();

        var options = configuration.GetSection(KokpitOptions.SectionName).Get<KokpitOptions>() ?? new KokpitOptions();
        services.AddAuthorization(o =>
        {
            o.AddPolicy(KokpitPolicies.Viewer, p => RequireRolesOrSignedIn(p, options.ViewerRoles));
            o.AddPolicy(KokpitPolicies.HrViewer, p => RequireRolesOrSignedIn(p, options.HrRoles));
        });
        return services;
    }

    private static void RequireRolesOrSignedIn(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder policy, string[] roles)
    {
        if (roles.Length > 0) policy.RequireRole(roles);
        else policy.RequireAuthenticatedUser();
    }
}

/// <summary>
/// Immutable, in-memory copy of every dataset the cockpit needs.
/// Loaded once by KokpitDataService and shared by ALL users; replaced as a whole on refresh.
/// Pages never query the database — they read from this object.
/// </summary>
public sealed class KokpitSnapshot
{
    public static KokpitSnapshot Empty { get; } = new();

    public DateTimeOffset LoadedAt { get; init; }
    public DateOnly AsOf { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    public IReadOnlyList<AuditRecord> Audits { get; init; } = [];
    public IReadOnlyList<ReportRecord> Reports { get; init; } = [];
    public IReadOnlyList<ReportStepRecord> ReportSteps { get; init; } = [];
    public IReadOnlyList<StepSlaRecord> Steps { get; init; } = [];
    public IReadOnlyList<FindingRecord> Findings { get; init; } = [];
    public IReadOnlyList<StaffRecord> Staff { get; init; } = [];
    public IReadOnlyList<StaffMovementRecord> Movements { get; init; } = [];
    public IReadOnlyList<PromotionRecord> Promotions { get; init; } = [];
    public IReadOnlyList<CapacityRecord> Capacity { get; init; } = [];

    public bool IsLoaded => LoadedAt != default;
}


/// <summary>
/// THE ONLY CLASS THAT TALKS TO THE DATABASE.
///
/// - Registered as a Singleton: one instance for the whole application.
/// - Loads all nine datasets in ONE round trip (QueryMultiple) into a <see cref="KokpitSnapshot"/>.
/// - KokpitRefreshWorker calls <see cref="RefreshAsync"/> periodically; the load is skipped when the
///   database change token has not moved, so idle periods cost one tiny query.
/// - Pages read <see cref="Current"/> and subscribe to <see cref="SnapshotUpdated"/>; they never query.
/// - On failure the last good snapshot is kept and <see cref="LastError"/> is set (UI shows "stale").
/// </summary>
public sealed class KokpitDataService(
    IKokpitConnectionFactory connectionFactory,
    IOptions<KokpitOptions> options,
    ILogger<KokpitDataService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile KokpitSnapshot _current = KokpitSnapshot.Empty;
    private long? _lastChangeToken;

    public KokpitSnapshot Current => _current;

    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public bool IsStale =>
        !_current.IsLoaded ||
        DateTimeOffset.Now - _current.LoadedAt > TimeSpan.FromMinutes(options.Value.StaleAfterMinutes);

    /// <summary>Raised after a new snapshot is published. Handlers run on a background thread: use InvokeAsync.</summary>
    public event Action<KokpitSnapshot>? SnapshotUpdated;

    /// <summary>Raised when a refresh attempt fails, so the UI can show the stale-data warning.</summary>
    public event Action? RefreshFailed;

    public async Task RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LastAttemptAt = DateTimeOffset.Now;
            var today = DateOnly.FromDateTime(DateTime.Today);

            await using var connection = connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);

            var token = await connection.ExecuteScalarAsync<long?>(
                new CommandDefinition(ChangeTokenSql, cancellationToken: cancellationToken));

            var dayChanged = _current.AsOf != today;
            if (!force && !dayChanged && _current.IsLoaded && token == _lastChangeToken)
                return;

            var snapshot = await LoadSnapshotAsync(connection, today, cancellationToken);

            _current = snapshot;
            _lastChangeToken = token;
            LastError = null;

            logger.LogInformation(
                "Cockpit snapshot loaded: {Audits} audits, {Reports} reports, {Findings} findings, {Staff} staff",
                snapshot.Audits.Count, snapshot.Reports.Count, snapshot.Findings.Count, snapshot.Staff.Count);

            SnapshotUpdated?.Invoke(snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            logger.LogError(ex, "Cockpit snapshot refresh failed; keeping the previous snapshot.");
            RefreshFailed?.Invoke();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<KokpitSnapshot> LoadSnapshotAsync(
        IDbConnection connection, DateOnly today, CancellationToken cancellationToken)
    {
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(AllDatasetsSql, commandTimeout: 60, cancellationToken: cancellationToken));

        var audits = (await grid.ReadAsync<AuditRecord>()).ToList();
        var reports = (await grid.ReadAsync<ReportRecord>()).ToList();
        var reportSteps = (await grid.ReadAsync<ReportStepRecord>()).ToList();
        var steps = (await grid.ReadAsync<StepSlaRecord>()).OrderBy(s => s.SortOrder).ToList();
        var findings = (await grid.ReadAsync<FindingRecord>()).ToList();
        var staff = (await grid.ReadAsync<StaffRecord>()).ToList();
        var movements = (await grid.ReadAsync<StaffMovementRecord>()).ToList();
        var promotions = (await grid.ReadAsync<PromotionRecord>()).ToList();
        var capacity = (await grid.ReadAsync<CapacityRecord>()).ToList();

        foreach (var finding in findings)
        {
            finding.AgingBucket = finding.IsOpen
                ? AgingBuckets.For(today.DayNumber - DateOnly.FromDateTime(finding.ReportDate).DayNumber)
                : null;
        }

        return new KokpitSnapshot
        {
            LoadedAt = DateTimeOffset.Now,
            AsOf = today,
            Audits = audits,
            Reports = reports,
            ReportSteps = reportSteps,
            Steps = steps,
            Findings = findings,
            Staff = staff,
            Movements = movements,
            Promotions = promotions,
            Capacity = capacity
        };
    }

    // ------------------------------------------------------------------------------------------
    // SQL. The views live in the reporting database (see Database/cockpit_views.sql).
    // Column names must match the model property names (Dapper maps by name).
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Cheap "has anything changed?" check. Requires SQL Server Change Tracking on the reporting DB.
    /// Alternative: SELECT MAX(LoadedAt) FROM cockpit.EtlRun if an ETL job fills the views.
    /// </summary>
    private const string ChangeTokenSql = "SELECT CHANGE_TRACKING_CURRENT_VERSION();";

    private const string AllDatasetsSql = """
        SELECT AuditId, AuditName, Area, Stage, PlannedStart, PlannedEnd, PlannedIssueDate, ActualEnd, IsDelayed
        FROM cockpit.vw_Audit
        WHERE YEAR(PlannedStart) = YEAR(GETDATE()) OR Stage <> 'Issued';

        SELECT ReportId, AuditId, AuditName, Area, AuditStage, CurrentStepCode, CurrentStepEnteredAt,
               IssuedAt, CycleDays, HasSkippedSteps
        FROM cockpit.vw_Report
        WHERE IssuedAt IS NULL OR IssuedAt >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1);

        SELECT s.ReportId, s.StepCode, s.EnteredAt, s.ExitedAt, s.WaitDays
        FROM cockpit.vw_ReportStep s
        JOIN cockpit.vw_Report r ON r.ReportId = s.ReportId
        WHERE r.IssuedAt IS NULL OR r.IssuedAt >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1);

        SELECT StepCode, StepName, SlaDays, SortOrder
        FROM cockpit.vw_StepSla;

        SELECT FindingId, AuditId, Area, Risk, ActionOwner, ReportDate, DueDate, ClosedDate, Status, IsRepeat
        FROM cockpit.vw_Finding
        WHERE Status <> 'Closed' OR ClosedDate >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1);

        SELECT EmployeeId, Title, Area, HireDate, TrainingHoursYtd, Certificates
        FROM cockpit.vw_Staff;

        SELECT EmployeeId, MovementDate, MovementType, MovementDetail, Title, Area
        FROM cockpit.vw_StaffMovement
        WHERE MovementDate >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1);

        SELECT EmployeeId, PeriodYear, FromTitle, ToTitle, DevelopmentScore, PromotionType, Area
        FROM cockpit.vw_Promotion
        WHERE PeriodYear = YEAR(GETDATE());

        SELECT Area, Month, PlannedManDays, ActualManDays, SurveyScore, SurveyCount
        FROM cockpit.vw_CapacityMonthly
        WHERE Month >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1);
        """;
}


/// <summary>
/// Background job that keeps the shared snapshot fresh.
/// One worker for the whole app = one database check per interval, no matter how many users are connected.
/// </summary>
public sealed class KokpitRefreshWorker(
    KokpitDataService dataService,
    IOptions<KokpitOptions> options,
    ILogger<KokpitRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First load at startup so the first user does not wait.
        await dataService.RefreshAsync(force: true, stoppingToken);

        var interval = TimeSpan.FromSeconds(Math.Max(30, options.Value.RefreshIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        logger.LogInformation("Cockpit refresh worker started, interval {Interval}.", interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await dataService.RefreshAsync(force: false, stoppingToken);
        }
    }
}


public interface IKokpitConnectionFactory
{
    DbConnection Create();
}

/// <summary>Creates connections to the reporting database (never the operational source systems).</summary>
public sealed class KokpitSqlConnectionFactory(IConfiguration configuration) : IKokpitConnectionFactory
{
    private readonly string _connectionString =
        configuration.GetConnectionString("KokpitReporting")
        ?? throw new InvalidOperationException("Connection string 'KokpitReporting' is missing.");

    public DbConnection Create() => new SqlConnection(_connectionString);
}
