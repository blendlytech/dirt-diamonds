using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>Row DTO mirroring Player_Person one-to-one (schema v11).</summary>
public struct PersonRow
{
    public string PlayerId;
    public double Gpa;
    public int Intelligence;
    public int Maturity;
    public int Happiness;
    public int Charisma;
    public int Confidence;
    public int Reputation;
    public int SocialStatus;
    public int Attractiveness;
    public int Teamwork;
    public int Morality;
    public int Discipline;
    public int WorkEthic;

    /// <summary>The schema's neutral defaults — what the v11 backfill writes.</summary>
    public static PersonRow Neutral(string playerId) => new()
    {
        PlayerId = playerId,
        Gpa = 2.5,
        Intelligence = 50,
        Maturity = 50,
        Happiness = 50,
        Charisma = 50,
        Confidence = 50,
        Reputation = 50,
        SocialStatus = 50,
        Attractiveness = 50,
        Teamwork = 50,
        Morality = 50,
        Discipline = 50,
        WorkEthic = 50,
    };
}

/// <summary>Row DTO mirroring Family_Background one-to-one (schema v11). Parent ids are null when the parent row was never generated or has been deleted (SET NULL).</summary>
public struct FamilyBackgroundRow
{
    public string PlayerId;
    public int WealthTier;
    public double HouseholdIncome;
    public string? Parent1Id;
    public string? Parent2Id;
    public bool HomeWifi;
    public double AllowanceWeekly;
    public int Strictness;
}

/// <summary>Row DTO mirroring Phone_State one-to-one (schema v11). No row = the pre-v11 phone (everything unlocked, no minutes accounting).</summary>
public struct PhoneStateRow
{
    public string PlayerId;
    public int Tier;
    public int Plan;
    public int MinutesRemaining;
    public int PurchasedDay;
}

/// <summary>Mirrors Player_Items.category's CHECK (1–5). Phones ride Phone_State, baseball gear quality rides Player_Equipment.</summary>
public enum ItemCategory : byte
{
    Transport = 1,
    Clothing = 2,
    Jewelry = 3,
    Food = 4,
    Gear = 5,
}

/// <summary>Row DTO mirroring Player_Items one-to-one (schema v11). item_id names an entry in the item catalog JSON, validated at catalog load.</summary>
public struct PlayerItemRow
{
    public string PlayerId;
    public string ItemId;
    public ItemCategory Category;
    public int AcquiredDay;
}

/// <summary>Row DTO mirroring Child_Development one-to-one (schema v11). No row = pure-nature heir (pre-v11 behavior).</summary>
public struct ChildDevelopmentRow
{
    public string ChildId;
    public int Care;
    public int Coaching;
    public int Funding;
    public int Neglect;
    public int LastTickDay;

    /// <summary>The schema's neutral defaults — what an absent row means (TryGetChild's false case), for callers (e.g. UI) that need a display value rather than a bool.</summary>
    public static ChildDevelopmentRow Neutral(string childId) => new()
    {
        ChildId = childId,
        Care = 50,
        Coaching = 50,
        Funding = 50,
        Neglect = 0,
    };
}

/// <summary>Row DTO mirroring Child_Rearing_Commitment one-to-one (schema v12). No row = 0 weekly funding (pre-v12 default).</summary>
public struct ChildRearingCommitmentRow
{
    public string PlayerId;
    public int WeeklyFunding;
}

/// <summary>
/// Typed query surface for the schema-v11 High School person layer
/// (Player_Person, Family_Background, Phone_State, Player_Items,
/// Child_Development). Same discipline as <see cref="NeedsQueries"/>:
/// compile-time-constant SQL, commands pooled and prepared once, per-call work
/// limited to parameter values, plain row DTOs only — simulation shapes stay
/// out of the Data layer, and the caller-side bridge (GameManager / the HS-2
/// creation path) converts between the two.
/// </summary>
public sealed class PersonQueries
{
    private const string SqlUpsertPerson =
        "INSERT INTO Player_Person (player_id, gpa, intelligence, maturity, happiness, charisma, confidence, " +
        "reputation, social_status, attractiveness, teamwork, morality, discipline, work_ethic) VALUES " +
        "(@playerId, @gpa, @intelligence, @maturity, @happiness, @charisma, @confidence, " +
        "@reputation, @socialStatus, @attractiveness, @teamwork, @morality, @discipline, @workEthic) " +
        "ON CONFLICT (player_id) DO UPDATE SET gpa = excluded.gpa, intelligence = excluded.intelligence, " +
        "maturity = excluded.maturity, happiness = excluded.happiness, charisma = excluded.charisma, " +
        "confidence = excluded.confidence, reputation = excluded.reputation, social_status = excluded.social_status, " +
        "attractiveness = excluded.attractiveness, teamwork = excluded.teamwork, morality = excluded.morality, " +
        "discipline = excluded.discipline, work_ethic = excluded.work_ethic;";

    private const string SqlSelectPerson =
        "SELECT player_id, gpa, intelligence, maturity, happiness, charisma, confidence, reputation, " +
        "social_status, attractiveness, teamwork, morality, discipline, work_ethic FROM Player_Person " +
        "WHERE player_id = @playerId;";

    private const string SqlSelectAllPersons =
        "SELECT player_id, gpa, intelligence, maturity, happiness, charisma, confidence, reputation, " +
        "social_status, attractiveness, teamwork, morality, discipline, work_ethic FROM Player_Person;";

    // HS-4 atomic per-stat adjusters (the PlayerQueries.AdjustFunds
    // discipline): deltas clamped in SQL against the columns' own CHECK
    // ranges, so the Life sim's delta flush composes with any other person
    // writer (e.g. ItemService's §3.1 in-batch reward) instead of
    // read-modify-write clobbering it. CONTRACT: the array index IS the
    // Simulation.Life.PersonStatId ordinal (that folder is Data-free, so the
    // mapping is mirrored, not referenced — the RelationshipKind precedent);
    // Tools/SchemaValidator pins each ordinal to its column round-trip.
    // Column names come from this fixed table, never caller input — building
    // the SQL by concatenation is injection-safe here by construction.
    private static readonly string[] AdjustStatColumns =
    {
        "intelligence",   // 0 = PersonStatId.Intelligence
        "maturity",       // 1 = PersonStatId.Maturity
        "happiness",      // 2 = PersonStatId.Happiness
        "charisma",       // 3 = PersonStatId.Charisma
        "confidence",     // 4 = PersonStatId.Confidence
        "reputation",     // 5 = PersonStatId.Reputation
        "social_status",  // 6 = PersonStatId.SocialStatus
        "attractiveness", // 7 = PersonStatId.Attractiveness
        "teamwork",       // 8 = PersonStatId.Teamwork
        "morality",       // 9 = PersonStatId.Morality
        "discipline",     // 10 = PersonStatId.Discipline
        "work_ethic",     // 11 = PersonStatId.WorkEthic
    };

    /// <summary>The number of adjustable integer person stats (gpa rides its own REAL adjuster).</summary>
    public const int AdjustableStatCount = 12;

    private static string AdjustStatSql(string column) =>
        $"UPDATE Player_Person SET {column} = MAX(0, MIN(100, {column} + @delta)) WHERE player_id = @playerId;";

    private const string SqlAdjustGpa =
        "UPDATE Player_Person SET gpa = MAX(0.0, MIN(4.0, gpa + @delta)) WHERE player_id = @playerId;";

    private const string SqlUpsertFamily =
        "INSERT INTO Family_Background (player_id, wealth_tier, household_income, parent1_id, parent2_id, " +
        "home_wifi, allowance_weekly, strictness) VALUES " +
        "(@playerId, @wealthTier, @householdIncome, @parent1Id, @parent2Id, @homeWifi, @allowanceWeekly, @strictness) " +
        "ON CONFLICT (player_id) DO UPDATE SET wealth_tier = excluded.wealth_tier, " +
        "household_income = excluded.household_income, parent1_id = excluded.parent1_id, " +
        "parent2_id = excluded.parent2_id, home_wifi = excluded.home_wifi, " +
        "allowance_weekly = excluded.allowance_weekly, strictness = excluded.strictness;";

    private const string SqlSelectFamily =
        "SELECT player_id, wealth_tier, household_income, parent1_id, parent2_id, home_wifi, " +
        "allowance_weekly, strictness FROM Family_Background WHERE player_id = @playerId;";

    private const string SqlUpsertPhone =
        "INSERT INTO Phone_State (player_id, tier, plan, minutes_remaining, purchased_day) VALUES " +
        "(@playerId, @tier, @plan, @minutesRemaining, @purchasedDay) " +
        "ON CONFLICT (player_id) DO UPDATE SET tier = excluded.tier, plan = excluded.plan, " +
        "minutes_remaining = excluded.minutes_remaining, purchased_day = excluded.purchased_day;";

    private const string SqlSelectPhone =
        "SELECT player_id, tier, plan, minutes_remaining, purchased_day FROM Phone_State WHERE player_id = @playerId;";

    // Owning the same catalog item twice is a wholesale no-op by design.
    private const string SqlAddItem =
        "INSERT OR IGNORE INTO Player_Items (player_id, item_id, category, acquired_day) VALUES " +
        "(@playerId, @itemId, @category, @acquiredDay);";

    private const string SqlRemoveItem =
        "DELETE FROM Player_Items WHERE player_id = @playerId AND item_id = @itemId;";

    private const string SqlSelectItemsFor =
        "SELECT player_id, item_id, category, acquired_day FROM Player_Items WHERE player_id = @playerId;";

    // Whole-table read for the boot-time catalog audit (items.json §5): the
    // table holds a handful of rows per person who owns anything, so a full
    // scan once per boot is trivial.
    private const string SqlSelectAllItems =
        "SELECT player_id, item_id, category, acquired_day FROM Player_Items;";

    private const string SqlUpsertChild =
        "INSERT INTO Child_Development (child_id, care, coaching, funding, neglect, last_tick_day) VALUES " +
        "(@childId, @care, @coaching, @funding, @neglect, @lastTickDay) " +
        "ON CONFLICT (child_id) DO UPDATE SET care = excluded.care, coaching = excluded.coaching, " +
        "funding = excluded.funding, neglect = excluded.neglect, last_tick_day = excluded.last_tick_day;";

    // HS-5 incremental nurture writer (the AdjustStat discipline): one atomic
    // clamped delta per axis, seeding a missing row from the axis's own
    // schema default so "first rearing event" and "hundredth" are the same
    // call. CONTRACT: the array index IS the Narrative ChildAxis ordinal
    // (mirrored, not referenced — the PersonStatId precedent); column names
    // and neutral seeds come from this fixed table, never caller input, so
    // the SQL concatenation is injection-safe by construction.
    private static readonly (string Column, int NeutralSeed)[] ChildAxisColumns =
    {
        ("care", 50),     // 0 = ChildAxis.Care
        ("coaching", 50), // 1 = ChildAxis.Coaching
        ("funding", 50),  // 2 = ChildAxis.Funding
        ("neglect", 0),   // 3 = ChildAxis.Neglect
    };

    /// <summary>The number of adjustable Child_Development axes.</summary>
    public const int ChildAxisCount = 4;

    private static string AdjustChildAxisSql(string column, int neutralSeed) =>
        $"INSERT INTO Child_Development (child_id, {column}, last_tick_day) " +
        $"VALUES (@childId, MAX(0, MIN(100, {neutralSeed} + @delta)), @day) " +
        $"ON CONFLICT (child_id) DO UPDATE SET {column} = MAX(0, MIN(100, {column} + @delta)), " +
        "last_tick_day = @day;";

    private const string SqlSelectChild =
        "SELECT child_id, care, coaching, funding, neglect, last_tick_day FROM Child_Development " +
        "WHERE child_id = @childId;";

    private const string SqlUpsertChildRearingCommitment =
        "INSERT INTO Child_Rearing_Commitment (player_id, weekly_funding) VALUES (@playerId, @weeklyFunding) " +
        "ON CONFLICT (player_id) DO UPDATE SET weekly_funding = excluded.weekly_funding;";

    private const string SqlSelectChildRearingCommitment =
        "SELECT player_id, weekly_funding FROM Child_Rearing_Commitment WHERE player_id = @playerId;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _upsertPerson;
    private readonly SqliteCommand _selectPerson;
    private readonly SqliteCommand _selectAllPersons;
    private readonly SqliteCommand[] _adjustStat;
    private readonly SqliteCommand _adjustGpa;
    private readonly SqliteCommand _upsertFamily;
    private readonly SqliteCommand _selectFamily;
    private readonly SqliteCommand _upsertPhone;
    private readonly SqliteCommand _selectPhone;
    private readonly SqliteCommand _addItem;
    private readonly SqliteCommand _removeItem;
    private readonly SqliteCommand _selectItemsFor;
    private readonly SqliteCommand _selectAllItems;
    private readonly SqliteCommand _upsertChild;
    private readonly SqliteCommand _selectChild;
    private readonly SqliteCommand[] _adjustChildAxis;
    private readonly SqliteCommand _upsertChildRearingCommitment;
    private readonly SqliteCommand _selectChildRearingCommitment;

    public PersonQueries(DatabaseManager db)
    {
        _db = db;

        _upsertPerson = db.GetPooledCommand(SqlUpsertPerson);
        if (_upsertPerson.Parameters.Count == 0)
        {
            _upsertPerson.Parameters.Add("@playerId", SqliteType.Text);
            _upsertPerson.Parameters.Add("@gpa", SqliteType.Real);
            _upsertPerson.Parameters.Add("@intelligence", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@maturity", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@happiness", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@charisma", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@confidence", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@reputation", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@socialStatus", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@attractiveness", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@teamwork", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@morality", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@discipline", SqliteType.Integer);
            _upsertPerson.Parameters.Add("@workEthic", SqliteType.Integer);
            _upsertPerson.Prepare();
        }

        _selectPerson = db.GetPooledCommand(SqlSelectPerson);
        if (_selectPerson.Parameters.Count == 0)
        {
            _selectPerson.Parameters.Add("@playerId", SqliteType.Text);
            _selectPerson.Prepare();
        }

        _selectAllPersons = db.GetPooledCommand(SqlSelectAllPersons);

        _adjustStat = new SqliteCommand[AdjustStatColumns.Length];
        for (int i = 0; i < AdjustStatColumns.Length; i++)
        {
            SqliteCommand command = db.GetPooledCommand(AdjustStatSql(AdjustStatColumns[i]));
            if (command.Parameters.Count == 0)
            {
                command.Parameters.Add("@delta", SqliteType.Integer);
                command.Parameters.Add("@playerId", SqliteType.Text);
                command.Prepare();
            }
            _adjustStat[i] = command;
        }

        _adjustGpa = db.GetPooledCommand(SqlAdjustGpa);
        if (_adjustGpa.Parameters.Count == 0)
        {
            _adjustGpa.Parameters.Add("@delta", SqliteType.Real);
            _adjustGpa.Parameters.Add("@playerId", SqliteType.Text);
            _adjustGpa.Prepare();
        }

        _upsertFamily = db.GetPooledCommand(SqlUpsertFamily);
        if (_upsertFamily.Parameters.Count == 0)
        {
            _upsertFamily.Parameters.Add("@playerId", SqliteType.Text);
            _upsertFamily.Parameters.Add("@wealthTier", SqliteType.Integer);
            _upsertFamily.Parameters.Add("@householdIncome", SqliteType.Real);
            _upsertFamily.Parameters.Add("@parent1Id", SqliteType.Text);
            _upsertFamily.Parameters.Add("@parent2Id", SqliteType.Text);
            _upsertFamily.Parameters.Add("@homeWifi", SqliteType.Integer);
            _upsertFamily.Parameters.Add("@allowanceWeekly", SqliteType.Real);
            _upsertFamily.Parameters.Add("@strictness", SqliteType.Integer);
            _upsertFamily.Prepare();
        }

        _selectFamily = db.GetPooledCommand(SqlSelectFamily);
        if (_selectFamily.Parameters.Count == 0)
        {
            _selectFamily.Parameters.Add("@playerId", SqliteType.Text);
            _selectFamily.Prepare();
        }

        _upsertPhone = db.GetPooledCommand(SqlUpsertPhone);
        if (_upsertPhone.Parameters.Count == 0)
        {
            _upsertPhone.Parameters.Add("@playerId", SqliteType.Text);
            _upsertPhone.Parameters.Add("@tier", SqliteType.Integer);
            _upsertPhone.Parameters.Add("@plan", SqliteType.Integer);
            _upsertPhone.Parameters.Add("@minutesRemaining", SqliteType.Integer);
            _upsertPhone.Parameters.Add("@purchasedDay", SqliteType.Integer);
            _upsertPhone.Prepare();
        }

        _selectPhone = db.GetPooledCommand(SqlSelectPhone);
        if (_selectPhone.Parameters.Count == 0)
        {
            _selectPhone.Parameters.Add("@playerId", SqliteType.Text);
            _selectPhone.Prepare();
        }

        _addItem = db.GetPooledCommand(SqlAddItem);
        if (_addItem.Parameters.Count == 0)
        {
            _addItem.Parameters.Add("@playerId", SqliteType.Text);
            _addItem.Parameters.Add("@itemId", SqliteType.Text);
            _addItem.Parameters.Add("@category", SqliteType.Integer);
            _addItem.Parameters.Add("@acquiredDay", SqliteType.Integer);
            _addItem.Prepare();
        }

        _removeItem = db.GetPooledCommand(SqlRemoveItem);
        if (_removeItem.Parameters.Count == 0)
        {
            _removeItem.Parameters.Add("@playerId", SqliteType.Text);
            _removeItem.Parameters.Add("@itemId", SqliteType.Text);
            _removeItem.Prepare();
        }

        _selectItemsFor = db.GetPooledCommand(SqlSelectItemsFor);
        if (_selectItemsFor.Parameters.Count == 0)
        {
            _selectItemsFor.Parameters.Add("@playerId", SqliteType.Text);
            _selectItemsFor.Prepare();
        }

        _selectAllItems = db.GetPooledCommand(SqlSelectAllItems);

        _upsertChild = db.GetPooledCommand(SqlUpsertChild);
        if (_upsertChild.Parameters.Count == 0)
        {
            _upsertChild.Parameters.Add("@childId", SqliteType.Text);
            _upsertChild.Parameters.Add("@care", SqliteType.Integer);
            _upsertChild.Parameters.Add("@coaching", SqliteType.Integer);
            _upsertChild.Parameters.Add("@funding", SqliteType.Integer);
            _upsertChild.Parameters.Add("@neglect", SqliteType.Integer);
            _upsertChild.Parameters.Add("@lastTickDay", SqliteType.Integer);
            _upsertChild.Prepare();
        }

        _selectChild = db.GetPooledCommand(SqlSelectChild);
        if (_selectChild.Parameters.Count == 0)
        {
            _selectChild.Parameters.Add("@childId", SqliteType.Text);
            _selectChild.Prepare();
        }

        _adjustChildAxis = new SqliteCommand[ChildAxisColumns.Length];
        for (int i = 0; i < ChildAxisColumns.Length; i++)
        {
            SqliteCommand command = db.GetPooledCommand(
                AdjustChildAxisSql(ChildAxisColumns[i].Column, ChildAxisColumns[i].NeutralSeed));
            if (command.Parameters.Count == 0)
            {
                command.Parameters.Add("@childId", SqliteType.Text);
                command.Parameters.Add("@delta", SqliteType.Integer);
                command.Parameters.Add("@day", SqliteType.Integer);
                command.Prepare();
            }
            _adjustChildAxis[i] = command;
        }

        _upsertChildRearingCommitment = db.GetPooledCommand(SqlUpsertChildRearingCommitment);
        if (_upsertChildRearingCommitment.Parameters.Count == 0)
        {
            _upsertChildRearingCommitment.Parameters.Add("@playerId", SqliteType.Text);
            _upsertChildRearingCommitment.Parameters.Add("@weeklyFunding", SqliteType.Integer);
            _upsertChildRearingCommitment.Prepare();
        }

        _selectChildRearingCommitment = db.GetPooledCommand(SqlSelectChildRearingCommitment);
        if (_selectChildRearingCommitment.Parameters.Count == 0)
        {
            _selectChildRearingCommitment.Parameters.Add("@playerId", SqliteType.Text);
            _selectChildRearingCommitment.Prepare();
        }
    }

    public void Upsert(in PersonRow row)
    {
        SqliteParameterCollection p = _upsertPerson.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@gpa"].Value = row.Gpa;
        p["@intelligence"].Value = row.Intelligence;
        p["@maturity"].Value = row.Maturity;
        p["@happiness"].Value = row.Happiness;
        p["@charisma"].Value = row.Charisma;
        p["@confidence"].Value = row.Confidence;
        p["@reputation"].Value = row.Reputation;
        p["@socialStatus"].Value = row.SocialStatus;
        p["@attractiveness"].Value = row.Attractiveness;
        p["@teamwork"].Value = row.Teamwork;
        p["@morality"].Value = row.Morality;
        p["@discipline"].Value = row.Discipline;
        p["@workEthic"].Value = row.WorkEthic;
        _db.ExecuteNonQuery(_upsertPerson);
    }

    /// <summary>Persists a whole tick's worth of person rows in one batch transaction (joins the caller's batch if one is open).</summary>
    public void BulkUpsert(ReadOnlySpan<PersonRow> rows)
    {
        bool ownBatch = !_db.IsBatchActive;
        if (ownBatch)
        {
            _db.BeginBatch();
        }
        try
        {
            foreach (ref readonly PersonRow row in rows)
            {
                Upsert(in row);
            }
            if (ownBatch)
            {
                _db.CommitBatch();
            }
        }
        catch
        {
            if (ownBatch)
            {
                _db.RollbackBatch();
            }
            throw;
        }
    }

    /// <summary>
    /// Atomically nudges one integer person stat, clamped into [0,100] in
    /// SQL. <paramref name="statIndex"/> is the PersonStatId ordinal (see
    /// AdjustStatColumns' contract comment); out-of-range throws loudly.
    /// A player with no row (impossible after the v11 boot backfill) is a
    /// silent zero-row no-op, matching AdjustFunds.
    /// </summary>
    public void AdjustStat(string playerId, int statIndex, int delta)
    {
        if (statIndex < 0 || statIndex >= AdjustStatColumns.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(statIndex),
                $"Person stat index {statIndex} is outside the 0–{AdjustStatColumns.Length - 1} PersonStatId range.");
        }
        SqliteCommand command = _adjustStat[statIndex];
        command.Parameters["@delta"].Value = delta;
        command.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(command);
    }

    /// <summary>Atomically nudges gpa, clamped into [0.0, 4.0] in SQL — the REAL twin of <see cref="AdjustStat"/>.</summary>
    public void AdjustGpa(string playerId, double delta)
    {
        _adjustGpa.Parameters["@delta"].Value = delta;
        _adjustGpa.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_adjustGpa);
    }

    public bool TryGet(string playerId, out PersonRow row)
    {
        _selectPerson.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectPerson);
        if (!reader.Read())
        {
            row = default;
            return false;
        }
        row = ReadPerson(reader);
        return true;
    }

    /// <summary>Bulk-loads every person row into <paramref name="destination"/> (cleared first), keyed by player_id.</summary>
    public int LoadAll(Dictionary<string, PersonRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAllPersons);
        while (reader.Read())
        {
            PersonRow row = ReadPerson(reader);
            destination[row.PlayerId] = row;
        }
        return destination.Count;
    }

    private static PersonRow ReadPerson(SqliteDataReader reader) => new()
    {
        PlayerId = reader.GetString(0),
        Gpa = reader.GetDouble(1),
        Intelligence = reader.GetInt32(2),
        Maturity = reader.GetInt32(3),
        Happiness = reader.GetInt32(4),
        Charisma = reader.GetInt32(5),
        Confidence = reader.GetInt32(6),
        Reputation = reader.GetInt32(7),
        SocialStatus = reader.GetInt32(8),
        Attractiveness = reader.GetInt32(9),
        Teamwork = reader.GetInt32(10),
        Morality = reader.GetInt32(11),
        Discipline = reader.GetInt32(12),
        WorkEthic = reader.GetInt32(13),
    };

    public void UpsertFamily(in FamilyBackgroundRow row)
    {
        SqliteParameterCollection p = _upsertFamily.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@wealthTier"].Value = row.WealthTier;
        p["@householdIncome"].Value = row.HouseholdIncome;
        p["@parent1Id"].Value = (object?)row.Parent1Id ?? DBNull.Value;
        p["@parent2Id"].Value = (object?)row.Parent2Id ?? DBNull.Value;
        p["@homeWifi"].Value = row.HomeWifi ? 1 : 0;
        p["@allowanceWeekly"].Value = row.AllowanceWeekly;
        p["@strictness"].Value = row.Strictness;
        _db.ExecuteNonQuery(_upsertFamily);
    }

    public bool TryGetFamily(string playerId, out FamilyBackgroundRow row)
    {
        _selectFamily.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectFamily);
        if (!reader.Read())
        {
            row = default;
            return false;
        }
        row = new FamilyBackgroundRow
        {
            PlayerId = reader.GetString(0),
            WealthTier = reader.GetInt32(1),
            HouseholdIncome = reader.GetDouble(2),
            Parent1Id = reader.IsDBNull(3) ? null : reader.GetString(3),
            Parent2Id = reader.IsDBNull(4) ? null : reader.GetString(4),
            HomeWifi = reader.GetInt32(5) != 0,
            AllowanceWeekly = reader.GetDouble(6),
            Strictness = reader.GetInt32(7),
        };
        return true;
    }

    public void UpsertPhone(in PhoneStateRow row)
    {
        SqliteParameterCollection p = _upsertPhone.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@tier"].Value = row.Tier;
        p["@plan"].Value = row.Plan;
        p["@minutesRemaining"].Value = row.MinutesRemaining;
        p["@purchasedDay"].Value = row.PurchasedDay;
        _db.ExecuteNonQuery(_upsertPhone);
    }

    public bool TryGetPhone(string playerId, out PhoneStateRow row)
    {
        _selectPhone.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectPhone);
        if (!reader.Read())
        {
            row = default;
            return false;
        }
        row = new PhoneStateRow
        {
            PlayerId = reader.GetString(0),
            Tier = reader.GetInt32(1),
            Plan = reader.GetInt32(2),
            MinutesRemaining = reader.GetInt32(3),
            PurchasedDay = reader.GetInt32(4),
        };
        return true;
    }

    public void AddItem(in PlayerItemRow row)
    {
        SqliteParameterCollection p = _addItem.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@itemId"].Value = row.ItemId;
        p["@category"].Value = (int)row.Category;
        p["@acquiredDay"].Value = row.AcquiredDay;
        _db.ExecuteNonQuery(_addItem);
    }

    /// <summary>Removes one owned item (the HS-5 parental-revoke path). Removing an item the player never owned is a no-op.</summary>
    public void RemoveItem(string playerId, string itemId)
    {
        SqliteParameterCollection p = _removeItem.Parameters;
        p["@playerId"].Value = playerId;
        p["@itemId"].Value = itemId;
        _db.ExecuteNonQuery(_removeItem);
    }

    /// <summary>Loads every Player_Items row in the save into <paramref name="destination"/> (cleared first) — the boot-time items.json ownership audit's read.</summary>
    public int LoadAllItems(List<PlayerItemRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAllItems);
        while (reader.Read())
        {
            destination.Add(new PlayerItemRow
            {
                PlayerId = reader.GetString(0),
                ItemId = reader.GetString(1),
                Category = (ItemCategory)reader.GetInt32(2),
                AcquiredDay = reader.GetInt32(3),
            });
        }
        return destination.Count;
    }

    /// <summary>Loads every item the player owns into <paramref name="destination"/> (cleared first).</summary>
    public int LoadItemsFor(string playerId, List<PlayerItemRow> destination)
    {
        destination.Clear();
        _selectItemsFor.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectItemsFor);
        while (reader.Read())
        {
            destination.Add(new PlayerItemRow
            {
                PlayerId = reader.GetString(0),
                ItemId = reader.GetString(1),
                Category = (ItemCategory)reader.GetInt32(2),
                AcquiredDay = reader.GetInt32(3),
            });
        }
        return destination.Count;
    }

    public void UpsertChild(in ChildDevelopmentRow row)
    {
        SqliteParameterCollection p = _upsertChild.Parameters;
        p["@childId"].Value = row.ChildId;
        p["@care"].Value = row.Care;
        p["@coaching"].Value = row.Coaching;
        p["@funding"].Value = row.Funding;
        p["@neglect"].Value = row.Neglect;
        p["@lastTickDay"].Value = row.LastTickDay;
        _db.ExecuteNonQuery(_upsertChild);
    }

    /// <summary>
    /// Atomically nudges one Child_Development axis, clamped into [0,100] in
    /// SQL, stamping last_tick_day. A child with no row gets one seeded from
    /// the axis's neutral default plus the delta (the other axes take their
    /// schema defaults), so accumulation never needs a separate ensure-row
    /// step. <paramref name="axisIndex"/> is the Narrative ChildAxis ordinal
    /// (see ChildAxisColumns' contract comment); out-of-range throws loudly.
    /// </summary>
    public void AdjustChildAxis(string childId, int axisIndex, int delta, long day)
    {
        if (axisIndex < 0 || axisIndex >= ChildAxisColumns.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(axisIndex),
                $"Child axis index {axisIndex} is outside the 0–{ChildAxisColumns.Length - 1} ChildAxis range.");
        }
        SqliteCommand command = _adjustChildAxis[axisIndex];
        command.Parameters["@childId"].Value = childId;
        command.Parameters["@delta"].Value = delta;
        command.Parameters["@day"].Value = day;
        _db.ExecuteNonQuery(command);
    }

    public bool TryGetChild(string childId, out ChildDevelopmentRow row)
    {
        _selectChild.Parameters["@childId"].Value = childId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectChild);
        if (!reader.Read())
        {
            row = default;
            return false;
        }
        row = new ChildDevelopmentRow
        {
            ChildId = reader.GetString(0),
            Care = reader.GetInt32(1),
            Coaching = reader.GetInt32(2),
            Funding = reader.GetInt32(3),
            Neglect = reader.GetInt32(4),
            LastTickDay = reader.GetInt32(5),
        };
        return true;
    }

    /// <summary>§7.1's player-set weekly funding commitment toward the avatar's children — a standing preference, upserted directly (not an atomic delta like AdjustChildAxis, since the player is replacing their commitment, not accumulating one).</summary>
    public void UpsertChildRearingCommitment(in ChildRearingCommitmentRow row)
    {
        SqliteParameterCollection p = _upsertChildRearingCommitment.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@weeklyFunding"].Value = row.WeeklyFunding;
        _db.ExecuteNonQuery(_upsertChildRearingCommitment);
    }

    public bool TryGetChildRearingCommitment(string playerId, out ChildRearingCommitmentRow row)
    {
        _selectChildRearingCommitment.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectChildRearingCommitment);
        if (!reader.Read())
        {
            row = default;
            return false;
        }
        row = new ChildRearingCommitmentRow
        {
            PlayerId = reader.GetString(0),
            WeeklyFunding = reader.GetInt32(1),
        };
        return true;
    }
}
