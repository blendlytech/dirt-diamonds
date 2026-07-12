namespace DirtAndDiamonds.Simulation.Life;

/// <summary>
/// Household board rule (docs/design/household_board.md): while the avatar is
/// a high-schooler with a Family_Background row — the same population
/// FamilyService's parentalSupport gate serves — the family covers a
/// wealth-tier share of "board": the Eat action's FinancialCost and the
/// avatar-only weekly cost-of-living bill, and nothing else. The table holds
/// the KID'S own share. Constants-as-data in the BackstoryProfile idiom;
/// the harness pins every cell.
/// </summary>
public static class HouseholdBoard
{
    /// <summary>
    /// Fraction of board costs the avatar pays himself, indexed by the §3
    /// wealth tier 0–4: Destitute pays in full (survival pressure is the dirt
    /// on-ramp), Working-class splits, Middle and up are covered.
    /// </summary>
    public static readonly double[] BoardShareByTier = { 1.0, 0.5, 0.0, 0.0, 0.0 };

    /// <summary>
    /// Share for a tier, where any value outside 0–4 — notably the -1
    /// "no household coverage" sentinel (pre-HS-2 saves, College/pro tiers,
    /// harness worlds) — pays in full. That 1.0 default is the slice's
    /// byte-identity guarantee: every pre-slice trace multiplies by 1.
    /// </summary>
    public static double ShareFor(int wealthTier) =>
        wealthTier >= 0 && wealthTier < BoardShareByTier.Length ? BoardShareByTier[wealthTier] : 1.0;
}
