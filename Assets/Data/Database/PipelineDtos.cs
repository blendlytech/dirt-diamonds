namespace DirtAndDiamonds.Data;

/// <summary>
/// The pipeline's engine-written <c>Entity_Flags</c> names (slice 13-6, brief
/// D3-A). Set and cleared only by the Baseball pipeline (and succession's
/// displacement); content never reads them in 13-6. They live in the Data
/// layer so the succession free-agent read (<see cref="BaseballQueries"/>) can
/// bind the retired flag as a parameter without referencing the sim.
/// </summary>
public static class PipelineFlags
{
    /// <summary>Active while a player is a Pro free agent; <c>set_on_day</c> = the day he became one.</summary>
    public const string FreeAgent = "pipeline_free_agent";

    /// <summary>A baseball career is over (retirement, an unrecruited senior, a retired free agent, a released avatar). Never signable again.</summary>
    public const string Retired = BaseballQueries.PipelineRetiredFlagName;

    /// <summary>Set at a player's first Pro contract; its absence makes a signing "first" for ProContractSignedEvent.</summary>
    public const string ProSigned = "pipeline_pro_signed";

    /// <summary>
    /// The avatar answered "stay in school" to the declaration decision
    /// (brief D5-A). <c>set_on_day</c> is the answer's day, so the season it
    /// belongs to is derivable and a reload keeps the answer (13-6a build
    /// record §11.3: the one flag beyond D3's three).
    /// </summary>
    public const string DeclarationDeclined = "pipeline_declaration_declined";
}

/// <summary>Recruiting_Offers.status values (schema v14): the numbers are load-bearing (the CHECK constraint).</summary>
public enum RecruitingOfferStatus : byte
{
    Open = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,
}

/// <summary>One College_Enrollment row (schema v14).</summary>
public struct CollegeEnrollmentRow
{
    public string PlayerId;
    public int TeamId;
    public int EntrySeason;
    public int ClassYear;
    public int? DeclaredSeason;
}

/// <summary>One Recruiting_Offers row (schema v14).</summary>
public struct RecruitingOfferRow
{
    public string PlayerId;
    public int TeamId;
    public int SeasonYear;
    public RecruitingOfferStatus Status;
}

/// <summary>One Draft_Picks row (schema v14).</summary>
public struct DraftPickRow
{
    public int SeasonYear;
    public int OverallPick;
    public int Round;
    public int TeamId;
    public string PlayerId;
}

/// <summary>
/// One Pro free agent (D3-A's pool: <c>team_id IS NULL</c> with an active
/// <see cref="PipelineFlags.FreeAgent"/> flag and a ratings row). The ratings
/// ride the roster join's shape (TeamId 0), plus the age, health and the
/// flag's <c>set_on_day</c> the retirement test reads.
/// </summary>
public struct FreeAgentRow
{
    public RosterPlayerRow Ratings;
    public int Age;
    public int HealthCeiling;
    public long SetOnDay;
}

/// <summary>
/// One player as the pipeline ranks him (brief C5): the roster join's ratings
/// and role next to age, <c>health_ceiling</c>, potential (headroom) and the
/// completed season's stat line. Built in memory from the bulk reads the
/// pipeline already makes; no new join.
/// </summary>
public struct PipelineCandidateRow
{
    public RosterPlayerRow Ratings;
    public int Age;
    public int HealthCeiling;
    public bool HasPotential;
    public PlayerPotentialRow Potential;
    public SeasonBattingLine Batting;
    public SeasonPitchingLine Pitching;

    /// <summary>The tier he played the completed season in (his current team's tier).</summary>
    public LeagueTier Tier;
}

/// <summary>One draft-board entry for <see cref="PipelineStatusView"/>.</summary>
public readonly record struct DraftBoardEntry(
    string PlayerId, int TeamId, PitcherRole Role, int ClassYear, int Age, double Value, int ProjectedSlot, bool IsAvatar);

/// <summary>
/// A read-only snapshot of the pipeline for the 13-6b phone cards and the
/// Draft board (and the harnesses): this season's class year, offers,
/// projected round, draft board and free agents. Presentation reads it and
/// sends intent through the pipeline's Resolve… calls; it never writes.
/// </summary>
public sealed class PipelineStatusView
{
    public int SeasonYear { get; init; }

    /// <summary>The avatar's College class year, or 0 when he is not enrolled.</summary>
    public int AvatarClassYear { get; init; }

    /// <summary>The avatar's projected slot on the current draft board (0 = ineligible or off the board).</summary>
    public int AvatarProjectedSlot { get; init; }

    /// <summary>1–4, or 0 when projected undrafted / ineligible.</summary>
    public int AvatarProjectedRound { get; init; }

    public IReadOnlyList<RecruitingOfferRow> AvatarOffers { get; init; } = Array.Empty<RecruitingOfferRow>();

    /// <summary>The draft-eligible board (class year ≥ 2), best first.</summary>
    public IReadOnlyList<DraftBoardEntry> DraftBoard { get; init; } = Array.Empty<DraftBoardEntry>();

    /// <summary>This season's Draft_Picks rows (empty before day 195).</summary>
    public IReadOnlyList<DraftPickRow> DraftPicks { get; init; } = Array.Empty<DraftPickRow>();

    public int FreeAgentBatters { get; init; }
    public int FreeAgentStarters { get; init; }
    public int FreeAgentRelievers { get; init; }
}
