using System.Data;
using Container.Components.Shared.Services;   // GlobalDegiskenler, IDataAccess — adjust if your namespace differs
using Microsoft.AspNetCore.Components.Authorization;

namespace Container.Components.Pages.Kokpit;

// =====================================================================================================
//  KokpitSetup.cs — cockpit settings and access rules.
//  No Program.cs or appsettings.json change is needed: everything the cockpit needs is in this folder.
// =====================================================================================================

public static class KokpitSettings
{
    /// <summary>Plan year shown in the footer. For now the current year; will come from the plan data later.</summary>
    public static int PlanYear => DateTime.Today.Year;

    /// <summary>Logo in the top bar, relative to wwwroot.</summary>
    public const string LogoPath = "img/kokpit/d3-white.png";

    public const string AccessDeniedRoute = "kokpit/access-denied";

    /// <summary>
    /// Users (Okyanus user codes, without "KUVEYTTURK\") who may open the cockpit.
    /// Temporary: in the data step this list moves to a table (e.g. WEB.KokpitYetki) so it can change without a deploy.
    /// </summary>
    public static readonly IReadOnlySet<string> ViewerUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // "kullanici1",
        // "kullanici2",
    };

    /// <summary>Users who may also see the HR tab and HR data. Must also be in ViewerUsers.</summary>
    public static readonly IReadOnlySet<string> HrUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // "kullanici1",
    };
}

/// <summary>What the current user may see in the cockpit. Cascaded to every tab.</summary>
public sealed record KokpitUserAccess(string? UserCode, string? FullName, bool CanView, bool CanSeeHr)
{
    public static readonly KokpitUserAccess None = new(null, null, false, false);
}

/// <summary>
/// Resolves the current user the same way the app's layouts do (Windows user → simulated user → Okyanus.UserInfo_SP),
/// then applies the cockpit lists. The page calls this itself because the layout loads the user asynchronously and
/// may not have finished when the page initialises.
/// </summary>
public static class KokpitAccess
{
    public static async Task<KokpitUserAccess> ResolveAsync(
        GlobalDegiskenler global, IDataAccess db, Task<AuthenticationState>? authenticationState)
    {
        // 1. Windows user (same rule as EmptyLayout)
        if (string.IsNullOrEmpty(global.User_Kullanici) && authenticationState is not null)
        {
            var identity = (await authenticationState).User.Identity;
            if (identity is { IsAuthenticated: true } && !string.IsNullOrEmpty(identity.Name))
                global.User_Kullanici = identity.Name.Replace("KUVEYTTURK\\", "", StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrEmpty(global.User_Kullanici))
            return KokpitUserAccess.None;

        // 2. Simulated user (WEB.SimuleEdilenKullanicilar_SP), as in every layout
        var simulated = await db.GetSingleRow<KokpitSimulatedUser, dynamic>(
            "WEB.SimuleEdilenKullanicilar_SP", new { Kullanici = global.User_Kullanici }, CommandType.StoredProcedure);
        if (!string.IsNullOrEmpty(simulated?.Kullanici))
            global.User_Kullanici = simulated.Kullanici;

        // 3. User details (fills _global.User_AdSoyad, User_Sector, ...)
        await global.KullaniciSatiri();

        var user = global.User_Kullanici;
        var canView = user is not null && KokpitSettings.ViewerUsers.Contains(user);
        var canSeeHr = canView && KokpitSettings.HrUsers.Contains(user!);

        return new KokpitUserAccess(user, global.User_AdSoyad, canView, canSeeHr);
    }

    private sealed class KokpitSimulatedUser
    {
        public string? KendiKullanicisiMi { get; set; }
        public string? Kullanici { get; set; }
    }
}
