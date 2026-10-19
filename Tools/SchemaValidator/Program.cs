using System.Diagnostics;
using System.Globalization;
using DirtAndDiamonds.Data;
using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Tools.SchemaValidator;

/// <summary>
/// Headless checking script behind the validate_sqlite_schema skill.
///
/// Default (scratch) mode: applies SchemaDefinitions.sql to a throwaway database
/// and proves the Phase 1 exit criteria — structural soundness, a 10k-player
/// round-trip through the real DatabaseManager/PlayerQueries code path, FK
/// integrity (rejection + cascade), and a benchmarked day-advance batch
/// transaction.
///
/// --live &lt;path&gt; mode: read-only structural audit of an existing save
/// (integrity_check, foreign_key_check, table/index/STRICT coverage). Never
/// mutates rows.
///
/// --emit &lt;path&gt; mode: (re)builds an empty, schema-applied scratch database
/// for the sqlite MCP (GEMINI.md §4: schema exploration targets a scratch copy,
/// never the live save). Refuses any path under the Godot save directory.
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();

    private static readonly string[] RequiredTables =
    {
        "Players", "Batting_Stats", "Pitching_Stats", "Relationships", "Entity_Flags", "Game_Logs", "Game_State",
        "Teams", "Player_Ratings", "Pitcher_Roles", "Pitch_Arsenals", "Life_Needs", "Life_Stress", "Team_Tiers",
        "Player_Absences", "Player_Equipment", "Player_Potential",
        "Player_Person", "Family_Background", "Phone_State", "Player_Items", "Child_Development",
        "Child_Rearing_Commitment", "Relationship_History",
        // Schema v14 (Phase 13, league_restructure.md §12; Player_Fielding, arcade §8.1).
        "Conferences", "Divisions", "Player_Fielding", "Schedule_Games", "Playoff_Series", "Team_Seasons",
        "Roster_Designations", "College_Enrollment", "Recruiting_Offers", "Draft_Picks",
        // Schema v14, slice 13-2 (brief D4): the starter-rest memory.
        "Pitcher_Rest",
    };

    private static int Main(string[] args)
    {
        string? livePath = null;
        string? emitPath = null;
        string? schemaPath = null;
        int playerCount = 10_000;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live" when i + 1 < args.Length:
                    livePath = args[++i];
                    break;
                case "--emit" when i + 1 < args.Length:
                    emitPath = args[++i];
                    break;
                case "--schema" when i + 1 < args.Length:
                    schemaPath = args[++i];
                    break;
                case "--players" when i + 1 < args.Length:
                    playerCount = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    Console.Error.WriteLine("Usage: SchemaValidator [--live <db path> | --emit <scratch db path>] [--schema <SchemaDefinitions.sql>] [--players <n>]");
                    return 2;
            }
        }

        try
        {
            if (emitPath is not null)
            {
                return EmitScratchDatabase(emitPath, schemaPath ?? FindSchemaScript());
            }
            if (livePath is not null)
            {
                RunLiveAudit(livePath);
            }
            else
            {
                schemaPath ??= FindSchemaScript();
                RunScratchValidation(schemaPath, playerCount);
                RunCleanBreakChecks(schemaPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 2;
        }

        int failed = Results.Count(r => !r.Pass);
        Console.WriteLine();
        foreach ((string name, bool pass, string detail) in Results)
        {
            Console.WriteLine($"[{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
        }
        Console.WriteLine();
        Console.WriteLine($"{Results.Count - failed}/{Results.Count} checks passed.");
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // Modes
    // ------------------------------------------------------------------

    private static int EmitScratchDatabase(string emitPath, string schemaPath)
    {
        string fullPath = Path.GetFullPath(emitPath);
        if (fullPath.Contains("app_userdata", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Refusing to emit under the Godot save directory: {fullPath}");
            return 2;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        DeleteDatabaseFiles(fullPath);
        int version;
        using (var db = new DatabaseManager(fullPath))
        {
            db.InitializeSchema(schemaPath);
            version = db.GetSchemaVersion();
        }
        SqliteConnection.ClearAllPools();
        Console.WriteLine($"Emitted scratch schema db (user_version={version}) from {schemaPath} to {fullPath}");
        return 0;
    }

    private static void RunScratchValidation(string schemaPath, int playerCount)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"dd_schema_validation_{Environment.ProcessId}.db");
        DeleteDatabaseFiles(dbPath);

        try
        {
            using var db = new DatabaseManager(dbPath);

            (string journalMode, bool foreignKeys, _) = db.GetConnectionDiagnostics();
            Check("WAL journal mode on open", journalMode.Equals("wal", StringComparison.OrdinalIgnoreCase), journalMode);
            Check("foreign_keys enforced on open", foreignKeys);

            db.InitializeSchema(schemaPath);
            Check("schema applies + user_version = 14", db.GetSchemaVersion() == 14, $"user_version={db.GetSchemaVersion()}");

            db.InitializeSchema(schemaPath);
            Check("schema re-apply is idempotent", true);

            RunStructuralChecks(db);

            var queries = new PlayerQueries(db);
            PlayerRow[] players = GeneratePlayers(playerCount);

            // --- 10k-player round-trip -------------------------------------
            var stopwatch = Stopwatch.StartNew();
            queries.BulkInsert(players);
            stopwatch.Stop();
            long insertMs = stopwatch.ElapsedMilliseconds;

            var loaded = new List<PlayerRow>(playerCount);
            stopwatch.Restart();
            int loadedCount = queries.LoadAll(loaded);
            stopwatch.Stop();

            Check($"bulk insert {playerCount:N0} players (single transaction)", true, $"{insertMs} ms");
            Check("bulk load count matches", loadedCount == playerCount, $"{loadedCount:N0} rows in {stopwatch.ElapsedMilliseconds} ms");
            Check("round-trip field fidelity", VerifyRoundTrip(players, loaded));

            // --- Satellite tables round-trip --------------------------------
            string subjectId = players[0].PlayerId;
            queries.InsertBattingSeason(new BattingStatsRow
            {
                PlayerId = subjectId, SeasonYear = 2026, Pa = 650, Ab = 580, H = 174,
                Doubles = 32, Triples = 3, Hr = 41, Bb = 60, So = 130, Rbi = 105, Sb = 12,
                Avg = 0.300, Obp = 0.366, Slg = 0.578, Ops = 0.944,
            });
            var seasons = new List<BattingStatsRow>();
            queries.LoadBattingSeasons(subjectId, seasons);
            Check("batting season round-trip", seasons.Count == 1 && seasons[0].Hr == 41 && Math.Abs(seasons[0].Ops - 0.944) < 1e-9);

            queries.SetFlag(subjectId, "compromised_syndicate", isActive: true, setOnDay: 812);
            queries.SetFlag(subjectId, "compromised_syndicate", isActive: true, setOnDay: 813); // upsert path
            var flags = new List<EntityFlagRow>();
            queries.LoadActiveFlags(subjectId, flags);
            Check("entity flag upsert round-trip", flags.Count == 1 && flags[0].SetOnDay == 813);

            queries.UpsertRelationship(players[1].PlayerId, players[2].PlayerId, -80, RelationshipType.Rival);
            queries.UpsertRelationship(players[2].PlayerId, players[1].PlayerId, -95, RelationshipType.Rival); // reversed pair → same row
            var rels = new List<RelationshipRow>();
            queries.LoadRelationshipsFor(players[1].PlayerId, rels);
            Check("relationship canonical-pair upsert", rels.Count == 1 && rels[0].AffinityScore == -95 && rels[0].Type == RelationshipType.Rival);

            // Schema v13: Relationship_History is append-only and OR IGNORE —
            // a reversed-pair re-insert and a straight re-insert both no-op.
            queries.InsertRelationshipHistory(players[3].PlayerId, players[4].PlayerId);
            queries.InsertRelationshipHistory(players[4].PlayerId, players[3].PlayerId); // reversed pair → same row
            queries.InsertRelationshipHistory(players[3].PlayerId, players[4].PlayerId); // exact repeat → no-op
            var history = new List<(string PlayerAId, string PlayerBId)>();
            queries.LoadAllRelationshipHistory(history);
            Check("relationship history is canonical-pair and OR-IGNORE idempotent", history.Count == 1);

            // --- Day-advance benchmark (before any deletions) ----------------
            stopwatch.Restart();
            db.RunInBatch(() =>
            {
                for (int i = 0; i < players.Length; i++)
                {
                    queries.UpdateFunds(players[i].PlayerId, players[i].Funds + 12.50);
                    queries.SetFlag(players[i].PlayerId, "daily_tick", isActive: true, setOnDay: 1);
                }
            });
            stopwatch.Stop();
            long tickMs = Math.Max(1, stopwatch.ElapsedMilliseconds);
            long statements = playerCount * 2L;
            Check("day-advance batch transaction", stopwatch.ElapsedMilliseconds < 5_000,
                $"{statements:N0} statements in {stopwatch.ElapsedMilliseconds} ms ({statements * 1000 / tickMs:N0} stmts/sec)");

            // --- Schema v4 role/arsenal tables (their backfill retired in v14) --
            // Runs after the benchmark: the cascade check deletes players[3].
            RunV4MigrationChecks(db, queries, schemaPath, players[3].PlayerId, players[4].PlayerId);

            // --- Schema v14: league structure, Player_Fielding, §12 tables ----
            // The fielding cascade check deletes players[9].
            RunV14Checks(db, queries, schemaPath, players[9].PlayerId, players[10].PlayerId);

            // --- Schema v11: High School person layer ------------------------
            // The cascade check deletes players[5]; players[6..8] survive.
            RunV11PersonLayerChecks(db, queries, schemaPath,
                players[5].PlayerId, players[6].PlayerId, players[7].PlayerId, players[8].PlayerId);

            // --- FK integrity ------------------------------------------------
            bool orphanRejected = false;
            try
            {
                queries.InsertBattingSeason(new BattingStatsRow { PlayerId = "ghost-no-such-player", SeasonYear = 2026 });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                orphanRejected = true;
            }
            Check("orphan stats row rejected (FK)", orphanRejected);
            Check("PRAGMA foreign_key_check clean", db.RunForeignKeyCheck() == 0);

            queries.Delete(subjectId);
            queries.LoadBattingSeasons(subjectId, seasons);
            queries.LoadActiveFlags(subjectId, flags);
            Check("delete cascades to stats + flags", seasons.Count == 0 && flags.Count == 0);

            bool checkRejected = false;
            try
            {
                queries.Insert(new PlayerRow
                {
                    PlayerId = Guid.NewGuid().ToString("D"), FirstName = "Bad", LastName = "Value",
                    Age = 30, Recklessness = 150, HealthCeiling = 100,
                });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                checkRejected = true;
            }
            Check("CHECK constraint rejects out-of-range stat", checkRejected);

            Check("PRAGMA integrity_check", db.RunIntegrityCheck() == "ok");
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    /// <summary>
    /// The schema v4 role/arsenal tables. Their v3→v4 backfill was RETIRED by
    /// the v14 clean break (13-1 brief §3.2.2/§5.4): a pre-v14 save is refused,
    /// never migrated, and every path that creates a pitcher writes its own
    /// role and arsenal rows. Retired checks, by name: "v4 backfill: pitcher
    /// role defaults to Starter", "v4 backfill: three arsenal rows, usage sums
    /// to 100", "v4 backfill: stuff-derived ratings (80 → FB 80/40, BRK 60/80,
    /// OFF 55/85)" and "v4 backfill is OR IGNORE (re-apply never clobbers
    /// existing rows)". Their replacement proves the removal; the CHECK and
    /// cascade checks below stay.
    /// </summary>
    private static void RunV4MigrationChecks(
        DatabaseManager db, PlayerQueries queries, string schemaPath, string pitcherId, string checkSubjectId)
    {
        // Stage a bare pitcher: a ratings row with no role/arsenal/potential rows.
        SqliteCommand insertRatings = db.GetPooledCommand(
            "INSERT INTO Player_Ratings (player_id, is_pitcher, pit_stuff) VALUES (@id, 1, @stuff);");
        if (insertRatings.Parameters.Count == 0)
        {
            insertRatings.Parameters.Add("@id", SqliteType.Text);
            insertRatings.Parameters.Add("@stuff", SqliteType.Integer);
        }
        insertRatings.Parameters["@id"].Value = pitcherId;
        insertRatings.Parameters["@stuff"].Value = 80;
        db.ExecuteNonQuery(insertRatings);

        db.InitializeSchema(schemaPath); // the boot-time schema pass

        Check("v14 clean break: re-apply backfills no role, arsenal or potential (v3→v4 / v9→v10 backfills retired)",
            ScalarLong(db, "SELECT COUNT(*) FROM Pitcher_Roles WHERE player_id = @id;", pitcherId) == 0 &&
            ScalarLong(db, "SELECT COUNT(*) FROM Pitch_Arsenals WHERE player_id = @id;", pitcherId) == 0 &&
            ScalarLong(db, "SELECT COUNT(*) FROM Player_Potential WHERE player_id = @id;", pitcherId) == 0);

        // Write the satellites the way every creation path now does.
        SqliteCommand stage = db.GetPooledCommand(
            "INSERT INTO Pitcher_Roles (player_id, role) VALUES (@id, 1); " +
            "INSERT INTO Pitch_Arsenals (player_id, pitch_type, velocity, movement, usage_weight) VALUES " +
            "(@id, 'Fastball', 80, 40, 60), (@id, 'Breaking', 60, 80, 25), (@id, 'Offspeed', 55, 85, 15);");
        if (stage.Parameters.Count == 0)
        {
            stage.Parameters.Add("@id", SqliteType.Text);
        }
        stage.Parameters["@id"].Value = pitcherId;
        db.ExecuteNonQuery(stage);

        // CHECK constraint coverage on the new tables.
        bool badRoleRejected = false;
        try
        {
            SqliteCommand badRole = db.GetPooledCommand(
                "INSERT INTO Pitcher_Roles (player_id, role) VALUES (@id, 7);");
            if (badRole.Parameters.Count == 0)
            {
                badRole.Parameters.Add("@id", SqliteType.Text);
            }
            // An existing player with no role row, so only the role CHECK can object.
            badRole.Parameters["@id"].Value = checkSubjectId;
            db.ExecuteNonQuery(badRole);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            badRoleRejected = true;
        }
        Check("v4 CHECK rejects unknown role / pitch_type", badRoleRejected);

        // Cascade: deleting the player clears role + arsenal satellites.
        queries.Delete(pitcherId);
        Check("v4 delete cascades to Pitcher_Roles + Pitch_Arsenals",
            ScalarLong(db, "SELECT COUNT(*) FROM Pitcher_Roles WHERE player_id = @id;", pitcherId) == 0 &&
            ScalarLong(db, "SELECT COUNT(*) FROM Pitch_Arsenals WHERE player_id = @id;", pitcherId) == 0);
    }

    /// <summary>
    /// Proves the schema-v11 High School person layer: the Player_Person
    /// neutral-row backfill (applies to every Players row, never clobbers a
    /// developed row), full DTO round-trips through PersonQueries for all five
    /// tables, the Family_Background parent SET NULL rule, the Player_Items
    /// INSERT OR IGNORE ownership rule, a CHECK rejection, and the
    /// delete-cascade across every satellite.
    /// </summary>
    private static void RunV11PersonLayerChecks(
        DatabaseManager db, PlayerQueries queries, string schemaPath,
        string subjectId, string parent1Id, string parent2Id, string survivorId)
    {
        var person = new PersonQueries(db);

        // Backfill: the 10k bulk insert ran after the initial schema apply, so
        // this re-apply is the boot that must heal every playerless row in.
        db.InitializeSchema(schemaPath);
        Check("v11 backfill: every player has a Player_Person row",
            ScalarLong(db, "SELECT COUNT(*) FROM Players WHERE player_id NOT IN (SELECT player_id FROM Player_Person);", subjectId) == 0);
        Check("v11 backfill row is neutral (gpa 2.5, stats 50)",
            person.TryGet(subjectId, out PersonRow neutral) &&
            Math.Abs(neutral.Gpa - 2.5) < 1e-9 && neutral.Intelligence == 50 && neutral.WorkEthic == 50);

        // Develop the row, re-apply, prove OR IGNORE left it alone.
        PersonRow developed = PersonRow.Neutral(subjectId);
        developed.Gpa = 3.7;
        developed.Morality = 82;
        developed.SocialStatus = 31;
        person.Upsert(in developed);
        db.InitializeSchema(schemaPath);
        Check("v11 backfill is OR IGNORE (re-apply never clobbers a developed row)",
            person.TryGet(subjectId, out PersonRow after) &&
            Math.Abs(after.Gpa - 3.7) < 1e-9 && after.Morality == 82 && after.SocialStatus == 31);

        // HS-4 atomic adjusters: the PersonStatId ordinal → column contract
        // is MIRRORED across the Data/Life wall (the Life folder is compiled
        // Data-free), so this is the check that pins the numbering — every
        // ordinal must move exactly its contracted column and nothing else.
        person.TryGet(subjectId, out PersonRow expectedRow);
        bool ordinalContractHolds = true;
        for (int s = 0; s < PersonQueries.AdjustableStatCount; s++)
        {
            person.AdjustStat(subjectId, s, 3);
            SetStatByOrdinal(ref expectedRow, s, GetStatByOrdinal(in expectedRow, s) + 3);
            if (!person.TryGet(subjectId, out PersonRow movedRow) || !PersonRowsEqual(in movedRow, in expectedRow))
            {
                ordinalContractHolds = false;
                break;
            }
        }
        Check("HS-4 AdjustStat: all 12 PersonStatId ordinals move exactly their contracted column (the Life-side enum mirror pinned)",
            ordinalContractHolds);

        person.AdjustStat(subjectId, 11, 500);  // work_ethic → far past the ceiling
        person.AdjustStat(subjectId, 9, -500);  // morality → far past the floor
        person.TryGet(subjectId, out PersonRow clampedRow);
        Check("HS-4 AdjustStat clamps in SQL to [0,100] — the CHECK constraint never trips",
            clampedRow.WorkEthic == 100 && clampedRow.Morality == 0);

        person.AdjustGpa(subjectId, -0.25);
        person.TryGet(subjectId, out PersonRow gpaNudged);
        bool gpaDeltaExact = Math.Abs(gpaNudged.Gpa - (after.Gpa - 0.25)) < 1e-9;
        person.AdjustGpa(subjectId, 99.0);
        person.TryGet(subjectId, out PersonRow gpaCeiling);
        person.AdjustGpa(subjectId, -99.0);
        person.TryGet(subjectId, out PersonRow gpaFloor);
        Check("HS-4 AdjustGpa: REAL delta lands exactly and clamps to [0.0, 4.0] in SQL",
            gpaDeltaExact && Math.Abs(gpaCeiling.Gpa - 4.0) < 1e-9 && Math.Abs(gpaFloor.Gpa) < 1e-9);

        bool badOrdinalThrows = false;
        try
        {
            person.AdjustStat(subjectId, PersonQueries.AdjustableStatCount, 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            badOrdinalThrows = true;
        }
        Check("HS-4 AdjustStat rejects an out-of-range ordinal loudly", badOrdinalThrows);

        // Family_Background round-trip + parent SET NULL.
        person.UpsertFamily(new FamilyBackgroundRow
        {
            PlayerId = subjectId, WealthTier = 1, HouseholdIncome = 28_500, Parent1Id = parent1Id,
            Parent2Id = parent2Id, HomeWifi = false, AllowanceWeekly = 10, Strictness = 65,
        });
        Check("v11 family round-trip",
            person.TryGetFamily(subjectId, out FamilyBackgroundRow family) &&
            family.WealthTier == 1 && family.Parent1Id == parent1Id && family.Parent2Id == parent2Id &&
            !family.HomeWifi && Math.Abs(family.AllowanceWeekly - 10) < 1e-9 && family.Strictness == 65);

        queries.Delete(parent1Id);
        Check("v11 parent delete SET NULLs the family pointer (row survives)",
            person.TryGetFamily(subjectId, out family) &&
            family.Parent1Id is null && family.Parent2Id == parent2Id && family.WealthTier == 1);

        // Phone_State round-trip; absent row stays absent (no backfill).
        Check("v11 phone: no row until one is written", !person.TryGetPhone(subjectId, out _));
        person.UpsertPhone(new PhoneStateRow
        {
            PlayerId = subjectId, Tier = 1, Plan = 0, MinutesRemaining = 120, PurchasedDay = 3,
        });
        Check("v11 phone round-trip",
            person.TryGetPhone(subjectId, out PhoneStateRow phone) &&
            phone.Tier == 1 && phone.Plan == 0 && phone.MinutesRemaining == 120 && phone.PurchasedDay == 3);

        // Player_Items: OR IGNORE ownership, load, remove.
        var items = new List<PlayerItemRow>();
        person.AddItem(new PlayerItemRow { PlayerId = subjectId, ItemId = "bmx_bike", Category = ItemCategory.Transport, AcquiredDay = 4 });
        person.AddItem(new PlayerItemRow { PlayerId = subjectId, ItemId = "bmx_bike", Category = ItemCategory.Transport, AcquiredDay = 9 }); // duplicate → no-op
        person.AddItem(new PlayerItemRow { PlayerId = subjectId, ItemId = "thrift_fit", Category = ItemCategory.Clothing, AcquiredDay = 5 });
        person.LoadItemsFor(subjectId, items);
        Check("v11 items: duplicate ownership is a no-op",
            items.Count == 2 && items.Exists(i => i.ItemId == "bmx_bike" && i.AcquiredDay == 4 && i.Category == ItemCategory.Transport));
        person.RemoveItem(subjectId, "thrift_fit");
        person.LoadItemsFor(subjectId, items);
        Check("v11 items: remove (parental revoke path)", items.Count == 1 && items[0].ItemId == "bmx_bike");

        // Child_Development round-trip.
        person.UpsertChild(new ChildDevelopmentRow
        {
            ChildId = subjectId, Care = 60, Coaching = 40, Funding = 30, Neglect = 10, LastTickDay = 12,
        });
        Check("v11 child-development round-trip",
            person.TryGetChild(subjectId, out ChildDevelopmentRow child) &&
            child.Care == 60 && child.Coaching == 40 && child.Funding == 30 && child.Neglect == 10 && child.LastTickDay == 12);

        // Child_Rearing_Commitment round-trip (schema v12).
        Check("v12 rearing commitment: no row until one is written", !person.TryGetChildRearingCommitment(subjectId, out _));
        person.UpsertChildRearingCommitment(new ChildRearingCommitmentRow { PlayerId = subjectId, WeeklyFunding = 75 });
        Check("v12 rearing commitment round-trip",
            person.TryGetChildRearingCommitment(subjectId, out ChildRearingCommitmentRow commitment) &&
            commitment.WeeklyFunding == 75);

        // CHECK coverage on the new tables.
        bool badTierRejected = false;
        try
        {
            person.UpsertFamily(new FamilyBackgroundRow { PlayerId = survivorId, WealthTier = 5 });
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            badTierRejected = true;
        }
        Check("v11 CHECK rejects out-of-range wealth_tier", badTierRejected);

        bool badFundingRejected = false;
        try
        {
            person.UpsertChildRearingCommitment(new ChildRearingCommitmentRow { PlayerId = survivorId, WeeklyFunding = 301 });
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            badFundingRejected = true;
        }
        Check("v12 CHECK rejects out-of-range weekly_funding", badFundingRejected);

        // Cascade: deleting the player clears every v11+v12 satellite.
        queries.Delete(subjectId);
        Check("v11+v12 delete cascades across all six person-layer tables",
            ScalarLong(db, "SELECT (SELECT COUNT(*) FROM Player_Person WHERE player_id = @id) + (SELECT COUNT(*) FROM Family_Background WHERE player_id = @id) + (SELECT COUNT(*) FROM Phone_State WHERE player_id = @id) + (SELECT COUNT(*) FROM Player_Items WHERE player_id = @id) + (SELECT COUNT(*) FROM Child_Development WHERE child_id = @id) + (SELECT COUNT(*) FROM Child_Rearing_Commitment WHERE player_id = @id);", subjectId) == 0);
    }

    /// <summary>
    /// Schema v14 (league_restructure.md §12, arcade §8.1): every §12 table and
    /// column introspected through pragma_table_info, the static league
    /// structure, the Teams v14 columns, Player_Fielding round-trip and cascade,
    /// and the CHECK/FK constraints the 13-1 brief §5.1 names. Each constraint
    /// check has a positive control, so a rejection is attributable to the
    /// constraint under test and not to a missing parent row.
    /// </summary>
    private static void RunV14Checks(
        DatabaseManager db, PlayerQueries queries, string schemaPath, string fieldingPlayerId, string otherPlayerId)
    {
        var baseball = new BaseballQueries(db);

        // --- §12 tables and columns (pragma_table_info) --------------------
        Check("v14 columns: Conferences", VerifyColumnTypes(db, "Conferences", new Dictionary<string, string>
        {
            ["conference_id"] = "INTEGER", ["tier"] = "INTEGER", ["name"] = "TEXT",
        }));
        Check("v14 columns: Divisions", VerifyColumnTypes(db, "Divisions", new Dictionary<string, string>
        {
            ["division_id"] = "INTEGER", ["conference_id"] = "INTEGER", ["name"] = "TEXT",
        }));
        Check("v14 columns: Teams (conference_id, division_id, home_park)", VerifyColumnTypes(db, "Teams", new Dictionary<string, string>
        {
            ["team_id"] = "INTEGER", ["city"] = "TEXT", ["name"] = "TEXT", ["abbreviation"] = "TEXT",
            ["conference_id"] = "INTEGER", ["division_id"] = "INTEGER", ["home_park"] = "TEXT",
        }));
        Check("v14 columns: Player_Fielding", VerifyColumnTypes(db, "Player_Fielding", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT", ["defensive_position"] = "TEXT", ["secondary_position"] = "TEXT",
            ["speed"] = "INTEGER", ["arm"] = "INTEGER",
        }));
        Check("v14 columns: Schedule_Games", VerifyColumnTypes(db, "Schedule_Games", new Dictionary<string, string>
        {
            ["game_id"] = "INTEGER", ["season_year"] = "INTEGER", ["tier"] = "INTEGER", ["day_of_season"] = "INTEGER",
            ["home_team_id"] = "INTEGER", ["away_team_id"] = "INTEGER", ["phase"] = "INTEGER", ["series_id"] = "INTEGER",
            ["game_in_series"] = "INTEGER", ["status"] = "INTEGER", ["home_score"] = "INTEGER", ["away_score"] = "INTEGER",
        }));
        Check("v14 columns: Playoff_Series", VerifyColumnTypes(db, "Playoff_Series", new Dictionary<string, string>
        {
            ["series_id"] = "INTEGER", ["season_year"] = "INTEGER", ["tier"] = "INTEGER", ["round"] = "INTEGER",
            ["best_of"] = "INTEGER", ["high_seed_team_id"] = "INTEGER", ["low_seed_team_id"] = "INTEGER",
            ["high_wins"] = "INTEGER", ["low_wins"] = "INTEGER", ["winner_team_id"] = "INTEGER",
        }));
        Check("v14 columns: Team_Seasons", VerifyColumnTypes(db, "Team_Seasons", new Dictionary<string, string>
        {
            ["team_id"] = "INTEGER", ["season_year"] = "INTEGER", ["wins"] = "INTEGER", ["losses"] = "INTEGER",
            ["runs_scored"] = "INTEGER", ["runs_allowed"] = "INTEGER", ["national_rank"] = "INTEGER", ["seed"] = "INTEGER",
        }));
        Check("v14 columns: Roster_Designations", VerifyColumnTypes(db, "Roster_Designations", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT", ["team_id"] = "INTEGER", ["is_active"] = "INTEGER", ["locked_through_day"] = "INTEGER",
        }));
        Check("v14 columns: College_Enrollment", VerifyColumnTypes(db, "College_Enrollment", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT", ["team_id"] = "INTEGER", ["entry_season"] = "INTEGER",
            ["class_year"] = "INTEGER", ["declared_season"] = "INTEGER",
        }));
        Check("v14 columns: Recruiting_Offers", VerifyColumnTypes(db, "Recruiting_Offers", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT", ["team_id"] = "INTEGER", ["season_year"] = "INTEGER", ["status"] = "INTEGER",
        }));
        Check("v14 columns: Draft_Picks", VerifyColumnTypes(db, "Draft_Picks", new Dictionary<string, string>
        {
            ["season_year"] = "INTEGER", ["overall_pick"] = "INTEGER", ["round"] = "INTEGER",
            ["team_id"] = "INTEGER", ["player_id"] = "TEXT",
        }));
        Check("index: Schedule_Games(season_year, tier, day_of_season) — the §12 ix_sched_day",
            HasIndexPrefix(db, "Schedule_Games", "season_year", "tier", "day_of_season"));
        Check("index: Player_Fielding(player_id)", HasIndexPrefix(db, "Player_Fielding", "player_id"));

        // --- Static league structure + Teams v14 columns -------------------
        baseball.EnsureLeagueStructure();
        baseball.EnsureLeagueStructure();
        var conferences = new List<ConferenceRow>();
        var divisions = new List<DivisionRow>();
        baseball.LoadConferences(conferences);
        baseball.LoadDivisions(divisions);
        bool twoPerTier = true;
        for (int t = 0; t < 3; t++)
        {
            twoPerTier &= conferences.Count(c => (int)c.Tier == t) == 2;
        }
        Check("v14 league structure: 6 conferences (2 per tier), 14 divisions, idempotent",
            conferences.Count == 6 && divisions.Count == 14 && twoPerTier,
            $"conferences={conferences.Count} divisions={divisions.Count}");

        TeamSeed hsSeed = LeagueStructure.Teams[0];   // 101, North, no division
        TeamSeed hsSeed2 = LeagueStructure.Teams[1];  // 102
        TeamSeed proSeed = LeagueStructure.Teams[^1]; // 332, Pacific
        baseball.InsertTeam(LeagueStructure.ToTeamRow(hsSeed, "Validator High Field"));
        baseball.UpsertTeamTier(hsSeed.TeamId, hsSeed.Tier);
        baseball.InsertTeam(LeagueStructure.ToTeamRow(hsSeed2, "Validator Second Field"));
        baseball.UpsertTeamTier(hsSeed2.TeamId, hsSeed2.Tier);
        baseball.InsertTeam(LeagueStructure.ToTeamRow(proSeed, "Validator Ballpark")); // no tier row on purpose
        var teams = new List<TeamRow>();
        baseball.LoadAllTeams(teams);
        TeamRow hs = teams.Find(t => t.TeamId == hsSeed.TeamId);
        TeamRow pro = teams.Find(t => t.TeamId == proSeed.TeamId);
        Check("v14 Teams round-trip: conference, NULL HS division, home park",
            hs.ConferenceId == 11 && hs.DivisionId is null && hs.HomePark == "Validator High Field" && hs.Tier == LeagueTier.HS &&
            pro.ConferenceId == 32 && pro.DivisionId == 324 && pro.HomePark == "Validator Ballpark");
        // Replaces the retired "pre-v7 tier defaults to MLB" reading (v6→v7 backfill).
        Check("v14: a team missing its Team_Tiers row reads as Pro (COALESCE 2)",
            baseball.TryGetTeamTier(proSeed.TeamId, out LeagueTier untiered) && untiered == LeagueTier.Pro
            && pro.Tier == LeagueTier.Pro);

        // --- CHECK constraints (each with a positive control) ---------------
        Check("v14 CHECK rejects Team_Tiers tier 3 (accepts 2)",
            Accepts(db, "UPDATE Team_Tiers SET tier = 2 WHERE team_id = 101;")
            && Rejects(db, "UPDATE Team_Tiers SET tier = 3 WHERE team_id = 101;")
            && Accepts(db, "UPDATE Team_Tiers SET tier = 0 WHERE team_id = 101;"));
        Check("v14 CHECK rejects Conferences tier 3",
            Rejects(db, "INSERT INTO Conferences (conference_id, tier, name) VALUES (99, 3, 'Minor');"));
        Check("v14 CHECK rejects a bad defensive_position (accepts 'C')",
            Rejects(db, "INSERT INTO Player_Fielding (player_id, defensive_position) VALUES (@id, 'DH');", fieldingPlayerId)
            && Accepts(db, "INSERT INTO Player_Fielding (player_id, defensive_position) VALUES (@id, 'C');", fieldingPlayerId));
        Check("v14 CHECK rejects a secondary_position equal to the primary",
            Rejects(db, "UPDATE Player_Fielding SET secondary_position = 'C' WHERE player_id = @id;", fieldingPlayerId));
        Check("v14 CHECK rejects best_of = 2 (accepts 3)",
            Rejects(db, "INSERT INTO Playoff_Series (series_id, season_year, tier, round, best_of, high_seed_team_id, low_seed_team_id) " +
                "VALUES (1, 2026, 0, 1, 2, 101, 102);")
            && Accepts(db, "INSERT INTO Playoff_Series (series_id, season_year, tier, round, best_of, high_seed_team_id, low_seed_team_id) " +
                "VALUES (1, 2026, 0, 1, 3, 101, 102);"));
        Check("v14 CHECK rejects Schedule_Games phase 3 and status 3 (accepts 2/2)",
            Rejects(db, "INSERT INTO Schedule_Games (game_id, season_year, tier, day_of_season, home_team_id, away_team_id, phase) " +
                "VALUES (1, 2026, 0, 64, 101, 102, 3);")
            && Rejects(db, "INSERT INTO Schedule_Games (game_id, season_year, tier, day_of_season, home_team_id, away_team_id, phase, status) " +
                "VALUES (1, 2026, 0, 64, 101, 102, 0, 3);")
            && Accepts(db, "INSERT INTO Schedule_Games (game_id, season_year, tier, day_of_season, home_team_id, away_team_id, phase, status, series_id, game_in_series) " +
                "VALUES (1, 2026, 0, 64, 101, 102, 2, 2, 1, 1);"));

        // --- FK constraints --------------------------------------------------
        Check("v14 FK rejects an orphan Player_Fielding row",
            Rejects(db, "INSERT INTO Player_Fielding (player_id, defensive_position) VALUES ('ghost-no-such-player', 'C');"));
        Check("v14 FK rejects a Teams row with an unknown conference",
            Rejects(db, "INSERT INTO Teams (team_id, city, name, abbreviation, conference_id, home_park) " +
                "VALUES (999, 'Nowhere', 'Ghosts', 'GHO', 77, 'Nowhere Park');"));
        Check("v14 NOT NULL rejects a Teams row without a home park",
            Rejects(db, "INSERT INTO Teams (team_id, city, name, abbreviation, conference_id) " +
                "VALUES (998, 'Nowhere', 'Ghosts', 'GHO', 11);"));
        Check("v14 FK rejects a Schedule_Games row naming an unknown team",
            Rejects(db, "INSERT INTO Schedule_Games (game_id, season_year, tier, day_of_season, home_team_id, away_team_id, phase) " +
                "VALUES (2, 2026, 0, 64, 101, 4242, 0);"));

        // --- Player_Fielding through the query layer --------------------------
        baseball.UpsertFielding(new PlayerFieldingRow
        {
            PlayerId = otherPlayerId, DefensivePosition = FieldingPosition.Shortstop,
            SecondaryPosition = null, Speed = 61, Arm = 47,
        });
        bool nullRoundTrip = baseball.TryGetFielding(otherPlayerId, out PlayerFieldingRow f1)
            && f1.DefensivePosition == FieldingPosition.Shortstop && f1.SecondaryPosition is null && f1.Speed == 61 && f1.Arm == 47;
        baseball.UpsertFielding(new PlayerFieldingRow
        {
            PlayerId = otherPlayerId, DefensivePosition = FieldingPosition.Shortstop,
            SecondaryPosition = FieldingPosition.SecondBase, Speed = 62, Arm = 48,
        });
        bool setRoundTrip = baseball.TryGetFielding(otherPlayerId, out PlayerFieldingRow f2)
            && f2.SecondaryPosition == FieldingPosition.SecondBase && f2.Speed == 62
            && ScalarLong(db, "SELECT COUNT(*) FROM Player_Fielding WHERE player_id = @id AND secondary_position = '2B';", otherPlayerId) == 1;
        Check("v14 Player_Fielding round-trip (NULL and '2B' secondary; upsert keeps one row)", nullRoundTrip && setRoundTrip);

        // ---- Pitcher_Rest (13-2, brief D4) ----
        Check("v14 columns: Pitcher_Rest", VerifyColumnTypes(db, "Pitcher_Rest", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT", ["last_start_day"] = "INTEGER",
        }));
        Check("index: Pitcher_Rest(player_id)", HasIndexPrefix(db, "Pitcher_Rest", "player_id"));
        db.RunInBatch(() =>
        {
            baseball.UpsertPitcherRest(fieldingPlayerId, 400);
            baseball.UpsertPitcherRest(fieldingPlayerId, 403);
            baseball.UpsertPitcherRest(otherPlayerId, 12);
        });
        var rest = new List<(string PlayerId, long LastStartDay)>();
        baseball.LoadAllPitcherRest(rest);
        Check("v14 Pitcher_Rest round-trip: upsert keeps one row per pitcher with the latest day",
            rest.Count == 2 && rest.Contains((fieldingPlayerId, 403L)) && rest.Contains((otherPlayerId, 12L)));
        Check("v14 FK rejects an orphan Pitcher_Rest row; CHECK rejects a negative day",
            Rejects(db, "INSERT INTO Pitcher_Rest (player_id, last_start_day) VALUES ('ghost-no-such-player', 1);")
            && Rejects(db, "UPDATE Pitcher_Rest SET last_start_day = -1 WHERE player_id = @id;", otherPlayerId));

        queries.Delete(fieldingPlayerId);
        Check("v14 delete cascades to Pitcher_Rest",
            ScalarLong(db, "SELECT COUNT(*) FROM Pitcher_Rest WHERE player_id = @id;", fieldingPlayerId) == 0);
        Check("v14 delete cascades to Player_Fielding",
            ScalarLong(db, "SELECT COUNT(*) FROM Player_Fielding WHERE player_id = @id;", fieldingPlayerId) == 0);

        db.InitializeSchema(schemaPath);
        Check("v14 re-apply keeps structure and fielding rows (idempotent)",
            ScalarLong(db, "SELECT COUNT(*) FROM Conferences WHERE @id = @id;", otherPlayerId) == 6
            && ScalarLong(db, "SELECT COUNT(*) FROM Player_Fielding WHERE player_id = @id;", otherPlayerId) == 1);
    }

    /// <summary>
    /// The v14 clean break end to end on scratch files (13-1 brief §3.2.5 and
    /// §5.1 "Clean break"), through the exact <see cref="SaveCompatibility"/>
    /// code GameManager runs. Uses its own temp folder, never the live save.
    /// </summary>
    private static void RunCleanBreakChecks(string schemaPath)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"dd_cleanbreak_{Environment.ProcessId}");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(dir);
        string save = Path.Combine(dir, "dirt_and_diamonds.db");
        string bak = Path.Combine(dir, "dirt_and_diamonds.v13.bak");
        string bak1 = Path.Combine(dir, "dirt_and_diamonds.v13.1.bak");
        string bak2 = Path.Combine(dir, "dirt_and_diamonds.v13.2.bak");
        string bak3 = Path.Combine(dir, "dirt_and_diamonds.v13.3.bak");

        try
        {
            Check("clean break: a missing save is a fresh world (no refusal)",
                SaveCompatibility.Inspect(save, out int v0) == SaveRefusal.None && v0 == 0 && !File.Exists(save));

            File.WriteAllBytes(save, Array.Empty<byte>());
            Check("clean break: a 0-byte save is a fresh world (no refusal, file untouched)",
                SaveCompatibility.Inspect(save, out int vz) == SaveRefusal.None && vz == 0
                && File.Exists(save) && new FileInfo(save).Length == 0);
            DeleteDatabaseFiles(save);

            WriteVersionedSave(save, 0, withMarker: false);
            Check("clean break: an empty SQLite file (user_version 0) is a fresh world",
                SaveCompatibility.Inspect(save, out int ve) == SaveRefusal.None && ve == 0);
            DeleteDatabaseFiles(save);

            WriteVersionedSave(save, 13, withMarker: true);
            string original = FileHash(save);
            SaveRefusal older = SaveCompatibility.Inspect(save, out int v13);
            Check("clean break: a v13 save is refused (OlderSave) and untouched until confirm",
                older == SaveRefusal.OlderSave && v13 == 13 && FileHash(save) == original && !File.Exists(bak),
                $"refusal={older} version={v13}");

            string moved = SaveCompatibility.ConfirmCleanBreak(save);
            Check("clean break: confirm renames the save to .v13.bak with its original bytes (hash match)",
                moved == bak && File.Exists(bak) && FileHash(bak) == original
                && !File.Exists(save) && !File.Exists(save + "-wal") && !File.Exists(save + "-shm"));

            int fresh;
            using (var db = new DatabaseManager(save))
            {
                db.InitializeSchema(schemaPath);
                fresh = db.GetSchemaVersion();
            }
            Check("clean break: boot then opens a new save at v14 (no refusal)",
                fresh == 14 && SaveCompatibility.Inspect(save, out _) == SaveRefusal.None);
            DeleteDatabaseFiles(save);

            WriteVersionedSave(save, 13, withMarker: true);
            string second = FileHash(save);
            string moved1 = SaveCompatibility.ConfirmCleanBreak(save);
            Check("clean break: an existing .v13.bak is never overwritten (the next one is .v13.1.bak)",
                moved1 == bak1 && FileHash(bak) == original && FileHash(bak1) == second);

            File.WriteAllBytes(save, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(save + "-wal", new byte[] { 4, 5 });
            File.WriteAllBytes(save + "-shm", new byte[] { 6 });
            string moved2 = SaveCompatibility.MoveAside(save);
            Check("clean break: -wal/-shm sidecars move with the save under the same suffix",
                moved2 == bak2
                && File.ReadAllBytes(bak2).SequenceEqual(new byte[] { 1, 2, 3 })
                && File.ReadAllBytes(bak2 + "-wal").SequenceEqual(new byte[] { 4, 5 })
                && File.ReadAllBytes(bak2 + "-shm").SequenceEqual(new byte[] { 6 })
                && !File.Exists(save) && !File.Exists(save + "-wal") && !File.Exists(save + "-shm"));

            WriteVersionedSave(save, 15, withMarker: true);
            string newer = FileHash(save);
            SaveRefusal newerRefusal = SaveCompatibility.Inspect(save, out int v15);
            bool confirmThrew = false;
            try
            {
                SaveCompatibility.ConfirmCleanBreak(save);
            }
            catch (InvalidOperationException)
            {
                confirmThrew = true;
            }
            Check("clean break: a v15 save is refused (NewerSave), never renamed, bytes unchanged",
                newerRefusal == SaveRefusal.NewerSave && v15 == 15 && confirmThrew
                && File.Exists(save) && FileHash(save) == newer && !File.Exists(bak3));
            DeleteDatabaseFiles(save);

            WriteVersionedSave(save, 14, withMarker: true);
            Check("clean break: a v14 save loads (no refusal)",
                SaveCompatibility.Inspect(save, out int v14) == SaveRefusal.None && v14 == 14);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // A lingering handle only leaves temp files behind; never fatal.
            }
        }
    }

    /// <summary>A scratch save at <paramref name="version"/>, written and closed through the game's own manager.</summary>
    private static void WriteVersionedSave(string path, int version, bool withMarker)
    {
        using var db = new DatabaseManager(path);
        db.ApplySchema(withMarker
            ? $"CREATE TABLE IF NOT EXISTS Marker (x INTEGER) STRICT; INSERT INTO Marker VALUES ({version}); PRAGMA user_version = {version};"
            : $"PRAGMA user_version = {version};");
    }

    private static string FileHash(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>True when <paramref name="sql"/> fails with SQLITE_CONSTRAINT (CHECK, FK or NOT NULL).</summary>
    private static bool Rejects(DatabaseManager db, string sql, string? playerId = null)
    {
        try
        {
            Execute(db, sql, playerId);
            return false;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return true;
        }
    }

    /// <summary>The positive control: true when <paramref name="sql"/> succeeds.</summary>
    private static bool Accepts(DatabaseManager db, string sql, string? playerId = null)
    {
        try
        {
            Execute(db, sql, playerId);
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void Execute(DatabaseManager db, string sql, string? playerId)
    {
        SqliteCommand cmd = db.GetPooledCommand(sql);
        if (playerId is not null)
        {
            if (cmd.Parameters.Count == 0)
            {
                cmd.Parameters.Add("@id", SqliteType.Text);
            }
            cmd.Parameters["@id"].Value = playerId;
        }
        db.ExecuteNonQuery(cmd);
    }

    // HS-4: PersonRow accessors keyed by the PersonStatId ordinal (0 =
    // intelligence … 11 = work_ethic, the Player_Person column order after
    // gpa) — the harness-side half of the mirrored contract under test.
    private static int GetStatByOrdinal(in PersonRow row, int ordinal) => ordinal switch
    {
        0 => row.Intelligence,
        1 => row.Maturity,
        2 => row.Happiness,
        3 => row.Charisma,
        4 => row.Confidence,
        5 => row.Reputation,
        6 => row.SocialStatus,
        7 => row.Attractiveness,
        8 => row.Teamwork,
        9 => row.Morality,
        10 => row.Discipline,
        11 => row.WorkEthic,
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal)),
    };

    private static void SetStatByOrdinal(ref PersonRow row, int ordinal, int value)
    {
        switch (ordinal)
        {
            case 0: row.Intelligence = value; break;
            case 1: row.Maturity = value; break;
            case 2: row.Happiness = value; break;
            case 3: row.Charisma = value; break;
            case 4: row.Confidence = value; break;
            case 5: row.Reputation = value; break;
            case 6: row.SocialStatus = value; break;
            case 7: row.Attractiveness = value; break;
            case 8: row.Teamwork = value; break;
            case 9: row.Morality = value; break;
            case 10: row.Discipline = value; break;
            case 11: row.WorkEthic = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(ordinal));
        }
    }

    private static bool PersonRowsEqual(in PersonRow a, in PersonRow b)
    {
        if (Math.Abs(a.Gpa - b.Gpa) > 1e-9)
        {
            return false;
        }
        for (int s = 0; s < PersonQueries.AdjustableStatCount; s++)
        {
            if (GetStatByOrdinal(in a, s) != GetStatByOrdinal(in b, s))
            {
                return false;
            }
        }
        return true;
    }

    private static long ScalarLong(DatabaseManager db, string sql, string playerId)
    {
        SqliteCommand cmd = db.GetPooledCommand(sql);
        if (cmd.Parameters.Count == 0)
        {
            cmd.Parameters.Add("@id", SqliteType.Text);
        }
        cmd.Parameters["@id"].Value = playerId;
        return Convert.ToInt64(db.ExecuteScalar(cmd) ?? -1L, CultureInfo.InvariantCulture);
    }

    private static void RunLiveAudit(string livePath)
    {
        if (!File.Exists(livePath))
        {
            Check("live database exists", false, livePath);
            return;
        }

        using var db = new DatabaseManager(livePath);
        (string journalMode, bool foreignKeys, int version) = db.GetConnectionDiagnostics();
        Check("WAL journal mode", journalMode.Equals("wal", StringComparison.OrdinalIgnoreCase), journalMode);
        Check("foreign_keys enforced", foreignKeys);
        Check("schema version set", version >= 1, $"user_version={version}");
        Check("PRAGMA integrity_check", db.RunIntegrityCheck() == "ok");
        Check("PRAGMA foreign_key_check clean", db.RunForeignKeyCheck() == 0);
        RunStructuralChecks(db);
    }

    // ------------------------------------------------------------------
    // Structural checks (shared by both modes)
    // ------------------------------------------------------------------

    private static void RunStructuralChecks(DatabaseManager db)
    {
        foreach (string table in RequiredTables)
        {
            string? sql = TableSql(db, table);
            Check($"table {table} exists", sql is not null);
            if (sql is not null)
            {
                Check($"table {table} is STRICT", sql.Contains("STRICT", StringComparison.OrdinalIgnoreCase));
            }
        }

        // Mandated hot-path index coverage (leftmost-prefix rule).
        Check("index: Batting_Stats(player_id…)", HasIndexPrefix(db, "Batting_Stats", "player_id"));
        Check("index: Pitching_Stats(player_id…)", HasIndexPrefix(db, "Pitching_Stats", "player_id"));
        Check("index: Relationships(player_1_id…)", HasIndexPrefix(db, "Relationships", "player_1_id"));
        Check("index: Relationships(player_2_id…)", HasIndexPrefix(db, "Relationships", "player_2_id"));
        // Schema v13: Relationship_History mirrors Relationships' index shape —
        // the PK covers player_1_id, player_2_id needs its own for reverse walk.
        Check("index: Relationship_History(player_1_id…)", HasIndexPrefix(db, "Relationship_History", "player_1_id"));
        Check("index: Relationship_History(player_2_id…)", HasIndexPrefix(db, "Relationship_History", "player_2_id"));
        Check("index: Entity_Flags(player_id, flag_name)", HasIndexPrefix(db, "Entity_Flags", "player_id", "flag_name"));
        Check("index: Game_Logs(player_id…)", HasIndexPrefix(db, "Game_Logs", "player_id"));
        Check("index: Game_Logs(season_year, game_day)", HasIndexPrefix(db, "Game_Logs", "season_year", "game_day"));
        // Schema v3: macro-sim groups rosters by team_id (deferred-FK hot path).
        Check("index: Players(team_id)", HasIndexPrefix(db, "Players", "team_id"));
        // Schema v4: roster join + arsenal bulk load ride the PKs.
        Check("index: Pitcher_Roles(player_id)", HasIndexPrefix(db, "Pitcher_Roles", "player_id"));
        Check("index: Pitch_Arsenals(player_id, pitch_type)", HasIndexPrefix(db, "Pitch_Arsenals", "player_id", "pitch_type"));
        // Schema v11: person-layer probes + the player-scoped item load ride the PKs.
        Check("index: Player_Person(player_id)", HasIndexPrefix(db, "Player_Person", "player_id"));
        Check("index: Player_Items(player_id, item_id)", HasIndexPrefix(db, "Player_Items", "player_id", "item_id"));

        // Data-type consistency spot check on the hottest table.
        Check("Players column types", VerifyColumnTypes(db, "Players", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT",
            ["age"] = "INTEGER",
            ["funds"] = "REAL",
            ["health_ceiling"] = "INTEGER",
            ["detection_risk"] = "INTEGER",
        }));

        // Schema v3: baseball rating inputs for the PA outcome model.
        Check("Player_Ratings column types", VerifyColumnTypes(db, "Player_Ratings", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT",
            ["is_pitcher"] = "INTEGER",
            ["bat_power"] = "INTEGER",
            ["pit_stuff"] = "INTEGER",
            ["fielding"] = "INTEGER",
        }));

        // Schema v4: bullpen roles + pitch-type arsenals.
        Check("Pitcher_Roles column types", VerifyColumnTypes(db, "Pitcher_Roles", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT",
            ["role"] = "INTEGER",
        }));
        Check("Pitch_Arsenals column types", VerifyColumnTypes(db, "Pitch_Arsenals", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT",
            ["pitch_type"] = "TEXT",
            ["velocity"] = "INTEGER",
            ["movement"] = "INTEGER",
            ["usage_weight"] = "INTEGER",
        }));

        // Schema v11: the person layer's hottest surfaces.
        Check("Player_Person column types", VerifyColumnTypes(db, "Player_Person", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT",
            ["gpa"] = "REAL",
            ["intelligence"] = "INTEGER",
            ["social_status"] = "INTEGER",
            ["work_ethic"] = "INTEGER",
        }));
        Check("Family_Background column types", VerifyColumnTypes(db, "Family_Background", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT",
            ["wealth_tier"] = "INTEGER",
            ["household_income"] = "REAL",
            ["parent1_id"] = "TEXT",
            ["home_wifi"] = "INTEGER",
        }));
        Check("Player_Items column types", VerifyColumnTypes(db, "Player_Items", new Dictionary<string, string>
        {
            ["player_id"] = "TEXT",
            ["item_id"] = "TEXT",
            ["category"] = "INTEGER",
            ["acquired_day"] = "INTEGER",
        }));
    }

    private static string? TableSql(DatabaseManager db, string table)
    {
        SqliteCommand cmd = db.GetPooledCommand(
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @name;");
        if (cmd.Parameters.Count == 0)
        {
            cmd.Parameters.Add("@name", SqliteType.Text);
        }
        cmd.Parameters["@name"].Value = table;
        return db.ExecuteScalar(cmd) as string;
    }

    /// <summary>
    /// True when <paramref name="table"/> has any index (auto or explicit) whose
    /// leading columns are exactly <paramref name="prefix"/>. Uses the pragma
    /// table-valued functions so the table name stays a bound parameter.
    /// </summary>
    private static bool HasIndexPrefix(DatabaseManager db, string table, params string[] prefix)
    {
        SqliteCommand cmd = db.GetPooledCommand(
            "SELECT il.name, ii.seqno, ii.name FROM pragma_index_list(@table) AS il " +
            "JOIN pragma_index_info(il.name) AS ii ORDER BY il.name, ii.seqno;");
        if (cmd.Parameters.Count == 0)
        {
            cmd.Parameters.Add("@table", SqliteType.Text);
        }
        cmd.Parameters["@table"].Value = table;

        var indexColumns = new Dictionary<string, List<string>>();
        using (SqliteDataReader reader = db.ExecuteReader(cmd))
        {
            while (reader.Read())
            {
                string indexName = reader.GetString(0);
                if (!indexColumns.TryGetValue(indexName, out List<string>? cols))
                {
                    cols = new List<string>();
                    indexColumns[indexName] = cols;
                }
                if (!reader.IsDBNull(2))
                {
                    cols.Add(reader.GetString(2));
                }
            }
        }

        foreach (List<string> cols in indexColumns.Values)
        {
            if (cols.Count < prefix.Length)
            {
                continue;
            }
            bool match = true;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (!string.Equals(cols[i], prefix[i], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return true;
            }
        }
        return false;
    }

    private static bool VerifyColumnTypes(DatabaseManager db, string table, Dictionary<string, string> expected)
    {
        SqliteCommand cmd = db.GetPooledCommand(
            "SELECT name, type FROM pragma_table_info(@table);");
        if (cmd.Parameters.Count == 0)
        {
            cmd.Parameters.Add("@table", SqliteType.Text);
        }
        cmd.Parameters["@table"].Value = table;

        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (SqliteDataReader reader = db.ExecuteReader(cmd))
        {
            while (reader.Read())
            {
                actual[reader.GetString(0)] = reader.GetString(1);
            }
        }

        foreach ((string column, string type) in expected)
        {
            if (!actual.TryGetValue(column, out string? actualType) ||
                !string.Equals(actualType, type, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static PlayerRow[] GeneratePlayers(int count)
    {
        var random = new Random(1927); // deterministic runs
        var players = new PlayerRow[count];
        for (int i = 0; i < count; i++)
        {
            players[i] = new PlayerRow
            {
                PlayerId = Guid.NewGuid().ToString("D"),
                FirstName = $"First{i:D5}",
                LastName = $"Last{i:D5}",
                Age = random.Next(16, 46),
                TeamId = i % 7 == 0 ? null : random.Next(1, 31),
                Funds = Math.Round(random.NextDouble() * 10_000, 2),
                HealthCeiling = random.Next(60, 101),
                Recklessness = random.Next(0, 101),
                BaseballInterest = random.Next(0, 101),
                DetectionRisk = random.Next(0, 101),
            };
        }
        return players;
    }

    private static bool VerifyRoundTrip(PlayerRow[] source, List<PlayerRow> loaded)
    {
        if (source.Length != loaded.Count)
        {
            return false;
        }

        var byId = new Dictionary<string, PlayerRow>(loaded.Count);
        foreach (PlayerRow row in loaded)
        {
            byId[row.PlayerId] = row;
        }

        foreach (PlayerRow expected in source)
        {
            if (!byId.TryGetValue(expected.PlayerId, out PlayerRow actual) ||
                actual.FirstName != expected.FirstName ||
                actual.LastName != expected.LastName ||
                actual.Age != expected.Age ||
                actual.TeamId != expected.TeamId ||
                Math.Abs(actual.Funds - expected.Funds) > 1e-9 ||
                actual.HealthCeiling != expected.HealthCeiling ||
                actual.Recklessness != expected.Recklessness ||
                actual.BaseballInterest != expected.BaseballInterest ||
                actual.DetectionRisk != expected.DetectionRisk)
            {
                return false;
            }
        }
        return true;
    }

    private static string FindSchemaScript()
    {
        string relative = Path.Combine("Assets", "Data", "Database", "SchemaDefinitions.sql");
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), relative);
        if (File.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        throw new FileNotFoundException(
            "Could not locate Assets/Data/Database/SchemaDefinitions.sql above the tool's directory; pass --schema <path>.");
    }

    private static void DeleteDatabaseFiles(string dbPath)
    {
        foreach (string path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void Check(string name, bool pass, string detail = "")
    {
        Results.Add((name, pass, detail));
    }
}
