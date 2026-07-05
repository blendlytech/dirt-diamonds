namespace DirtAndDiamonds.Data;

/// <summary>
/// Relationship categories persisted in Relationships.type_enum. The database
/// stores the string form (see <see cref="RelationshipTypeMap"/>) so saves stay
/// human-readable; keep both in sync with the CHECK constraint in
/// SchemaDefinitions.sql.
/// </summary>
public enum RelationshipType : byte
{
    Rival = 0,
    Friend = 1,
    Partner = 2,
    Child = 3,
}

/// <summary>Allocation-free mapping between <see cref="RelationshipType"/> and its DB string.</summary>
public static class RelationshipTypeMap
{
    private const string Rival = "Rival";
    private const string Friend = "Friend";
    private const string Partner = "Partner";
    private const string Child = "Child";

    public static string ToDbString(RelationshipType type) => type switch
    {
        RelationshipType.Rival => Rival,
        RelationshipType.Friend => Friend,
        RelationshipType.Partner => Partner,
        RelationshipType.Child => Child,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static RelationshipType FromDbString(string value) => value switch
    {
        Rival => RelationshipType.Rival,
        Friend => RelationshipType.Friend,
        Partner => RelationshipType.Partner,
        Child => RelationshipType.Child,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown relationship type in database."),
    };
}

/// <summary>
/// Row DTOs mirror table columns one-to-one and carry no behavior. They are
/// structs so bulk loads land in contiguous arrays/lists for the data-oriented
/// sim loops (zero-GC mandate); the string fields are references into the
/// reader's decoded values, not copies per field access.
/// </summary>
public struct PlayerRow
{
    public string PlayerId;
    public string FirstName;
    public string LastName;
    public int Age;
    public int? TeamId;
    public double Funds;
    public int HealthCeiling;
    public int Recklessness;
    public int BaseballInterest;
    public int DetectionRisk;
}

public struct BattingStatsRow
{
    public long StatId;
    public string PlayerId;
    public int SeasonYear;
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

public struct PitchingStatsRow
{
    public long StatId;
    public string PlayerId;
    public int SeasonYear;
    public int G;
    public int Gs;
    public int W;
    public int L;
    public int Sv;
    public int OutsRecorded;
    public int HAllowed;
    public int Er;
    public int Bb;
    public int So;
    public double Ip;
    public double Era;
    public double Whip;
}

public struct RelationshipRow
{
    public long RelId;
    public string Player1Id;
    public string Player2Id;
    public int AffinityScore;
    public RelationshipType Type;
}

public struct EntityFlagRow
{
    public long FlagId;
    public string PlayerId;
    public string FlagName;
    public bool IsActive;
    public long? SetOnDay;
}

/// <summary>
/// Player_Absences.reason values (schema v8) — why a player is out of games.
/// The numeric values are load-bearing (the CHECK constraint and the
/// PlayerAbsenceChangedEvent's byte payload both carry them); None is the
/// query-layer value for "no row", never stored.
/// </summary>
public enum AbsenceReason : byte
{
    None = 0,
    Injury = 1,
    Suspension = 2,
    Arrest = 3,
}

/// <summary>
/// Row DTO for Player_Absences (schema v8). A player is absent while
/// current_day &lt; UntilDay; an injury additionally plays rusty (effective
/// ratings down RatingPenalty points) while UntilDay ≤ current_day &lt;
/// PenaltyUntilDay. RatingPenalty/PenaltyUntilDay are 0 for non-injuries.
/// </summary>
public struct PlayerAbsenceRow
{
    public string PlayerId;
    public AbsenceReason Reason;
    public long UntilDay;
    public int RatingPenalty;
    public long PenaltyUntilDay;
}

/// <summary>
/// Row DTO for Player_Equipment (schema v9). Quality is the owned gear tier
/// (1 = Quality, 2 = Premium, 3 = Custom Pro); quality 0 (standard issue) is
/// the query-layer value for "no row", never stored — the AbsenceReason.None
/// rule. Upgrade-only: the upsert no-ops a same-or-lower quality wholesale.
/// </summary>
public struct PlayerEquipmentRow
{
    public string PlayerId;
    public int Quality;
    public long PurchasedDay;
}
