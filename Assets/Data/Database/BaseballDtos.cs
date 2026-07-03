namespace DirtAndDiamonds.Data;

/// <summary>
/// Pitcher_Roles.role values (schema v4). None is the query-layer value for
/// position players, who have no role row; only 1 and 2 exist in the table.
/// </summary>
public enum PitcherRole
{
    None = 0,
    Starter = 1,
    Reliever = 2,
}

/// <summary>
/// Pitch_Arsenals.pitch_type values (schema v4), stored by name. The order is
/// load-bearing: arsenal arrays index by (int)PitchType.
/// </summary>
public enum PitchType : byte
{
    Fastball = 0,
    Breaking = 1,
    Offspeed = 2,
}

/// <summary>Row DTO for the Teams table (schema v3). Mirrors columns one-to-one.</summary>
public struct TeamRow
{
    public int TeamId;
    public string City;
    public string Name;
    public string Abbreviation;
    public string? League;
    public string? Division;
}

/// <summary>Row DTO for Player_Ratings (schema v3). 0–100 scale, 50 = league average.</summary>
public struct PlayerRatingsRow
{
    public string PlayerId;
    public bool IsPitcher;
    public int BatPower;
    public int BatContact;
    public int BatDiscipline;
    public int PitStuff;
    public int PitControl;
    public int PitStamina;
    public int Fielding;
}

/// <summary>
/// One row of the Players ⋈ Player_Ratings ⟕ Pitcher_Roles roster join —
/// everything the macro-sim needs per baseball-active player, bulk-loaded up
/// front (never mid-simulation). Role is None for position players (no
/// Pitcher_Roles row).
/// </summary>
public struct RosterPlayerRow
{
    public string PlayerId;
    public int TeamId;
    public bool IsPitcher;
    public PitcherRole Role;
    public int BatPower;
    public int BatContact;
    public int BatDiscipline;
    public int PitStuff;
    public int PitControl;
    public int PitStamina;
    public int Fielding;
}

/// <summary>Row DTO for Pitch_Arsenals (schema v4). 0–100 scales, 50 = league average.</summary>
public struct PitchArsenalRow
{
    public string PlayerId;
    public PitchType Type;
    public int Velocity;
    public int Movement;

    /// <summary>Selection share of the pitcher's mix; the three types sum to 100.</summary>
    public int UsageWeight;
}

/// <summary>League-wide Batting_Stats sums for one season (run_monte_carlo_batch acceptance math).</summary>
public struct LeagueBattingTotals
{
    public long Pa;
    public long Ab;
    public long H;
    public long Doubles;
    public long Triples;
    public long Hr;
    public long Bb;
    public long So;
    public long Rbi;
}

/// <summary>League-wide Pitching_Stats sums for one season. Runs/team-game = Er / Gs (all runs earned in the macro-sim).</summary>
public struct LeaguePitchingTotals
{
    public long G;
    public long Gs;
    public long W;
    public long L;
    public long OutsRecorded;
    public long HAllowed;
    public long Er;
    public long Bb;
    public long So;
}
