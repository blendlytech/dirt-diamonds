using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Platform.Steam;

/// <summary>
/// Slice 13-7 (docs/tasks/13-7_ripple_brief.md §4.4): the pure decisions of the
/// Steam layer — which wire token a tier or a career state maps to, and which
/// achievements a tier unlocks. No Facepunch, no Godot, no I/O: the file is
/// linked into Tools/MonteCarloHarness (which cannot compile the Facepunch-
/// facing Steam files) so the mapping is proven by a check instead of by eye.
/// <see cref="AchievementManager"/> and <see cref="RichPresenceWriter"/> call
/// these and add only the guarded <see cref="SteamIntegration"/> side effect.
///
/// Every string here is a wire contract, minted verbatim on the Steamworks
/// partner site (README.md in this folder), never display wording. They are
/// pinned consts, not derived from <see cref="LeagueTier"/>.ToString(): an enum
/// rename must never silently change the wire.
/// </summary>
internal static class SteamRules
{
    // ---- Rich presence ----

    public const string StatusCareerToken = "#Status_Career";
    public const string StatusBetweenCareersToken = "#Status_BetweenCareers";

    /// <summary>The parent phase: a released avatar with no team while the bloodline still holds the career (D3-A).</summary>
    public const string StatusFamilyToken = "#Status_Family";

    public const string TierTokenHS = "#Tier_HS";
    public const string TierTokenCollege = "#Tier_College";
    public const string TierTokenPro = "#Tier_Pro";

    /// <summary>
    /// The sub-token a tier publishes under the <c>tier</c> key. Schema v14 has
    /// exactly three tiers, so the switch is total; an out-of-range value falls
    /// through to Pro, the top rung, like the code it replaces.
    /// </summary>
    public static string TierToken(LeagueTier tier) => tier switch
    {
        LeagueTier.HS => TierTokenHS,
        LeagueTier.College => TierTokenCollege,
        _ => TierTokenPro,
    };

    /// <summary>
    /// The <c>steam_display</c> token for a session. A lineage that ended is
    /// between careers whatever else is true; otherwise an avatar on a roster
    /// is a career and an avatar without a team (released, parent phase, D3-A)
    /// is a family.
    /// </summary>
    public static string StatusToken(bool hasTeam, bool lineageOver) =>
        lineageOver ? StatusBetweenCareersToken
        : hasTeam ? StatusCareerToken
        : StatusFamilyToken;

    // ---- Achievements ----

    /// <summary>
    /// "Reach the Pro league." Fires on every re-assert while the avatar sits on
    /// a Pro roster; slice 17a redefines it as "play your first Pro game".
    /// </summary>
    public const string AchTheShow = "ACH_THE_SHOW";

    private static readonly string[] None = Array.Empty<string>();
    private static readonly string[] ProOnly = { AchTheShow };

    /// <summary>
    /// The achievements a tier unlocks (D1-A): Pro unlocks exactly one. The old
    /// Went Pro achievement is retired (schema v14 has one professional tier, so
    /// it could only ever double-fire with The Show). A span over shared static
    /// arrays: no allocation, and the caller cannot mutate them.
    /// </summary>
    public static ReadOnlySpan<string> TierAchievements(LeagueTier tier) => tier == LeagueTier.Pro ? ProOnly : None;
}
