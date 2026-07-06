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
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();

    private static readonly string[] RequiredTables =
    {
        "Players", "Batting_Stats", "Pitching_Stats", "Relationships", "Entity_Flags", "Game_Logs", "Game_State",
        "Teams", "Player_Ratings", "Pitcher_Roles", "Pitch_Arsenals", "Life_Needs", "Life_Stress", "Team_Tiers",
        "Player_Absences", "Player_Equipment", "Player_Potential",
    };

    private static int Main(string[] args)
    {
        string? livePath = null;
        string? schemaPath = null;
        int playerCount = 10_000;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live" when i + 1 < args.Length:
                    livePath = args[++i];
                    break;
                case "--schema" when i + 1 < args.Length:
                    schemaPath = args[++i];
                    break;
                case "--players" when i + 1 < args.Length:
                    playerCount = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    Console.Error.WriteLine("Usage: SchemaValidator [--live <db path>] [--schema <SchemaDefinitions.sql>] [--players <n>]");
                    return 2;
            }
        }

        try
        {
            if (livePath is not null)
            {
                RunLiveAudit(livePath);
            }
            else
            {
                schemaPath ??= FindSchemaScript();
                RunScratchValidation(schemaPath, playerCount);
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
            Check("schema applies + user_version = 10", db.GetSchemaVersion() == 10, $"user_version={db.GetSchemaVersion()}");

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

            // --- Schema v4: role/arsenal backfill migration ------------------
            // Runs after the benchmark: the cascade check deletes players[3].
            RunV4MigrationChecks(db, queries, schemaPath, players[3].PlayerId, players[4].PlayerId);

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
    /// Proves the v3→v4 pure-SQL migration: a pitcher who exists in
    /// Player_Ratings without role/arsenal rows (the v3 state) gains a Starter
    /// role and the 60/25/15 stuff-derived arsenal when the idempotent script
    /// re-applies — and a re-apply never overwrites rows that already exist
    /// (INSERT OR IGNORE), so post-v4 generated arsenals survive every boot.
    /// </summary>
    private static void RunV4MigrationChecks(
        DatabaseManager db, PlayerQueries queries, string schemaPath, string pitcherId, string checkSubjectId)
    {
        // Stage the v3 state: ratings row flagged as pitcher, no satellite rows.
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

        db.InitializeSchema(schemaPath); // the boot-time migration pass

        Check("v4 backfill: pitcher role defaults to Starter",
            ScalarLong(db, "SELECT COALESCE((SELECT role FROM Pitcher_Roles WHERE player_id = @id), -1);", pitcherId) == 1);

        Check("v4 backfill: three arsenal rows, usage sums to 100",
            ScalarLong(db, "SELECT COUNT(*) FROM Pitch_Arsenals WHERE player_id = @id;", pitcherId) == 3 &&
            ScalarLong(db, "SELECT SUM(usage_weight) FROM Pitch_Arsenals WHERE player_id = @id;", pitcherId) == 100);

        Check("v4 backfill: stuff-derived ratings (80 → FB 80/40, BRK 60/80, OFF 55/85)",
            ScalarLong(db, "SELECT velocity FROM Pitch_Arsenals WHERE player_id = @id AND pitch_type = 'Fastball';", pitcherId) == 80 &&
            ScalarLong(db, "SELECT movement FROM Pitch_Arsenals WHERE player_id = @id AND pitch_type = 'Breaking';", pitcherId) == 80 &&
            ScalarLong(db, "SELECT velocity FROM Pitch_Arsenals WHERE player_id = @id AND pitch_type = 'Offspeed';", pitcherId) == 55 &&
            ScalarLong(db, "SELECT movement FROM Pitch_Arsenals WHERE player_id = @id AND pitch_type = 'Offspeed';", pitcherId) == 85);

        // Perturb, re-apply, and prove OR IGNORE left the existing rows alone.
        SqliteCommand perturb = db.GetPooledCommand(
            "UPDATE Pitcher_Roles SET role = 2 WHERE player_id = @id; " +
            "UPDATE Pitch_Arsenals SET velocity = 99 WHERE player_id = @id AND pitch_type = 'Fastball';");
        if (perturb.Parameters.Count == 0)
        {
            perturb.Parameters.Add("@id", SqliteType.Text);
        }
        perturb.Parameters["@id"].Value = pitcherId;
        db.ExecuteNonQuery(perturb);

        db.InitializeSchema(schemaPath);
        Check("v4 backfill is OR IGNORE (re-apply never clobbers existing rows)",
            ScalarLong(db, "SELECT role FROM Pitcher_Roles WHERE player_id = @id;", pitcherId) == 2 &&
            ScalarLong(db, "SELECT velocity FROM Pitch_Arsenals WHERE player_id = @id AND pitch_type = 'Fastball';", pitcherId) == 99);

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
        Check("index: Entity_Flags(player_id, flag_name)", HasIndexPrefix(db, "Entity_Flags", "player_id", "flag_name"));
        Check("index: Game_Logs(player_id…)", HasIndexPrefix(db, "Game_Logs", "player_id"));
        Check("index: Game_Logs(season_year, game_day)", HasIndexPrefix(db, "Game_Logs", "season_year", "game_day"));
        // Schema v3: macro-sim groups rosters by team_id (deferred-FK hot path).
        Check("index: Players(team_id)", HasIndexPrefix(db, "Players", "team_id"));
        // Schema v4: roster join + arsenal bulk load ride the PKs.
        Check("index: Pitcher_Roles(player_id)", HasIndexPrefix(db, "Pitcher_Roles", "player_id"));
        Check("index: Pitch_Arsenals(player_id, pitch_type)", HasIndexPrefix(db, "Pitch_Arsenals", "player_id", "pitch_type"));

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
