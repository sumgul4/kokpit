using Microsoft.AspNetCore.Authorization;

namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitSetup.cs — settings, authorization policies and one-line registration of the cockpit.
//  Program.cs:  builder.Services.AddKokpit(builder.Configuration);
// =====================================================================================================

/// <summary>Bound from appsettings.json → "Kokpit" section.</summary>
public sealed class KokpitOptions
{
    public const string SectionName = "Kokpit";

    /// <summary>AD groups allowed to open the cockpit. Empty = nobody (fail closed).</summary>
    public string[] ViewerRoles { get; set; } = [];

    /// <summary>AD groups allowed to see the HR tab and HR data. Empty = nobody.</summary>
    public string[] HrRoles { get; set; } = [];

    /// <summary>Plan year shown in the footer. Null = current calendar year (will come from data later).</summary>
    public int? PlanYear { get; set; }

    /// <summary>Logo in the top bar, relative to wwwroot.</summary>
    public string LogoPath { get; set; } = "img/kokpit/d3-white.png";

    public int EffectivePlanYear => PlanYear ?? DateTime.Today.Year;
}

public static class KokpitPolicies
{
    /// <summary>May open /kokpit and every tab except HR.</summary>
    public const string Viewer = "KokpitViewer";

    /// <summary>May see the HR tab and HR data (checked in addition to Viewer).</summary>
    public const string HrViewer = "KokpitHrViewer";
}

public static class KokpitRoutes
{
    public const string Home = "kokpit";
    public const string AccessDenied = "kokpit/access-denied";
}

public static class KokpitSetup
{
    public static IServiceCollection AddKokpit(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(KokpitOptions.SectionName);
        services.Configure<KokpitOptions>(section);

        var options = section.Get<KokpitOptions>() ?? new KokpitOptions();
        services.AddAuthorization(o =>
        {
            o.AddPolicy(KokpitPolicies.Viewer, p => RequireAnyRole(p, options.ViewerRoles));
            o.AddPolicy(KokpitPolicies.HrViewer, p => RequireAnyRole(p, options.HrRoles));
        });

        return services;
    }

    // Fail closed: if no role is configured, nobody gets in.
    private static void RequireAnyRole(AuthorizationPolicyBuilder policy, string[] roles)
    {
        policy.RequireAuthenticatedUser();
        if (roles.Length > 0) policy.RequireRole(roles);
        else policy.RequireAssertion(_ => false);
    }
}
