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

/// <summary>
/// Team_Tiers.tier values (schema v7) — the career-ladder dimension. The
/// numeric order IS the ladder order (Phase 9c's promotion gates climb it),
/// and arrays index by (int)tier, so the order is load-bearing.
/// </summary>
public enum LeagueTier : byte
{
    HS = 0,
    College = 1,
    MinorA = 2,
    MinorAA = 3,
    MinorAAA = 4,
    MLB = 5,
}

/// <summary>
/// Row DTO for the Teams table (schema v3) plus the team's ladder tier
/// (schema v7, joined from Team_Tiers; a missing tier row reads as MLB,
/// matching the v6→v7 backfill).
/// </summary>
public struct TeamRow
{
    public int TeamId;
    public string City;
    public string Name;
    public string Abbreviation;
    public string? League;
    public string? Division;
    public LeagueTier Tier;
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
/// Row DTO for Player_Potential (schema v10) — the latent per-rating ceiling
/// the 9d offseason development pass grows each rating toward. Mirrors
/// <see cref="PlayerRatingsRow"/> minus is_pitcher (potential is a ceiling on
/// the same seven 0–100 scales, not a role). The invariant potential_i ≥
/// current_i holds at creation by construction (backfill copies current;
/// intake discounts current below potential) and is preserved by the
/// development curve's never-overshoot clamp.
/// </summary>
public struct PlayerPotentialRow
{
    public string PlayerId;
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
    public string FirstName;
    public string LastName;
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

/// <summary>
/// One player's Batting_Stats counting line for a single season — the 9c
/// promotion pass's per-player performance read (OPS is recomputed in C# from
/// these counts with the exact SqlNormalizeBattingRates formula, so the score
/// never depends on whether the rate denormalization has run).
/// </summary>
public struct SeasonBattingLine
{
    public string PlayerId;
    public int Pa;
    public int Ab;
    public int H;
    public int Doubles;
    public int Triples;
    public int Hr;
    public int Bb;
}

/// <summary>One player's Pitching_Stats counting line for a single season (9c promotion pass; ERA = 27·ER/outs).</summary>
public struct SeasonPitchingLine
{
    public string PlayerId;
    public int OutsRecorded;
    public int Er;
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

/// <summary>
/// One player's current-season Batting_Stats row, rate columns included
/// (12c-2 StatLineCard) — unlike <see cref="SeasonBattingLine"/> (the 9c
/// promotion pass's counting-only read across every player), this is a
/// single-player probe that also carries the denormalized avg/obp/slg/ops
/// StatsNormalizer already wrote, so the card never recomputes rates itself.
/// </summary>
public struct BattingSeasonLine
{
    public int Pa;
    public int Ab;
    public int H;
    public int Doubles;
    public int Triples;
    public int Hr;
    public int Bb;
    public int So;
    public int Rbi;
    public int Sb;
    public double Avg;
    public double Obp;
    public double Slg;
    public double Ops;
}

/// <summary>One player's current-season Pitching_Stats row, rates included (12c-2 StatLineCard counterpart of <see cref="BattingSeasonLine"/>).</summary>
public struct PitchingSeasonLine
{
    public int G;
    public int Gs;
    public int W;
    public int L;
    public int Sv;
    public double Ip;
    public double Era;
    public double Whip;
    public int So;
    public int Bb;
}

/// <summary>
/// One team's season W/L, tier-scoped (12c-3 StandingsCard) — the read
/// recovered without a schema change per surface_the_sim.md §2: a team's
/// record is exactly SUM(w)/SUM(l) over the Pitching_Stats rows of its
/// CURRENTLY rostered players, since <c>LeagueSimulator</c> credits the
/// decision to exactly one starter per macro game. Team identity/name
/// resolves separately via <see cref="TeamRow"/> (<c>LoadTeamsByTier</c>);
/// win pct and games-behind are UI presentation math, computed in C# after
/// merging the two, not stored here.
/// </summary>
public struct TeamRecordRow
{
    public int TeamId;
    public int Wins;
    public int Losses;
}

/// <summary>
/// One player's rank-row value for a league-leaders category (12c-3
/// LeadersCard) — shared shape across all six leaderboards (HR/AVG/OPS
/// batting, ERA/W/SO pitching) since every one is a (name, value) pair; the
/// category's meaning (count vs. rate, higher-or-lower-is-better) lives in
/// the query that produced the row, not in this DTO.
/// </summary>
public struct LeagueLeaderRow
{
    public string PlayerId;
    public string FirstName;
    public string LastName;
    public double Value;
}
