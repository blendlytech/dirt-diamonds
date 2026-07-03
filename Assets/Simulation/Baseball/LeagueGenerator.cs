using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One-time world-gen for the background league: seeds Teams, Players and
/// Player_Ratings when the save has no league yet (new game). Deterministic
/// under a fixed <see cref="RngState"/> seed. Runs once per save — this is
/// load-time code, not simulation-loop code, so ordinary allocation is fine.
/// </summary>
public static class LeagueGenerator
{
    /// <summary>Bell-ish rating spread around 50 used for a fresh game world.
    /// 0 generates a perfectly average league (the §8 calibration roster).</summary>
    public const int DefaultRatingSpread = 25;

    private static readonly (string City, string Name, string Abbr, string Division)[] TeamSeeds =
    {
        ("Bayport", "Barons", "BAY", "East"),
        ("Kingsbridge", "Krakens", "KBR", "East"),
        ("Harborview", "Herons", "HBV", "East"),
        ("Ironvale", "Foxes", "IRV", "East"),
        ("Dustfield", "Drifters", "DST", "West"),
        ("Mesa Roja", "Rattlers", "MRJ", "West"),
        ("Silverlake", "Sharks", "SLK", "West"),
        ("Cascadia", "Wolves", "CAS", "West"),
    };

    private static readonly string[] FirstNames =
    {
        "Jack", "Marcus", "Tyler", "Andre", "Luis", "Pedro", "Kenji", "Sam",
        "Cole", "Derek", "Felix", "Omar", "Trent", "Victor", "Wade", "Xavier",
        "Yusuke", "Zack", "Rafael", "Nolan", "Mateo", "Isaiah", "Hank", "Gus",
    };

    private static readonly string[] LastNames =
    {
        "Alvarez", "Brooks", "Carter", "Delgado", "Ellis", "Fontaine", "Grady", "Hoshino",
        "Irwin", "Jenkins", "Kowalski", "Lopez", "Mercer", "Novak", "Ortega", "Pruitt",
        "Quintana", "Reyes", "Sandoval", "Tanaka", "Underwood", "Vaughn", "Whitaker", "Yoder",
    };

    /// <summary>
    /// Relievers roll the same rating bell as everyone else, then give this
    /// much stamina back — bullpen arms are shorter-burst by construction
    /// (capacity ≈ 85 at an average roll instead of 95).
    /// </summary>
    public const int RelieverStaminaPenalty = 20;

    /// <summary>
    /// Seeds the full league (8 teams × 9 position players + 5 starters + 3
    /// relievers, schema v4) in one batch transaction when Teams is empty.
    /// Returns false when a league already exists (normal boot on an existing
    /// save). The 9+5 loop draws exactly the v3 sequence from the caller's
    /// stream — every M1 calibration line is byte-for-byte reproducible — and
    /// all v4 additions (relievers, arsenals) draw from a Split() fork.
    /// </summary>
    public static bool GenerateIfEmpty(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng)
    {
        if (baseball.CountTeams() > 0)
        {
            return false;
        }

        // (id, stuff) of every pitcher in generation order, for the arsenal pass.
        var pitchers = new List<(string Id, int Stuff)>(
            TeamSeeds.Length * (LeagueSimulator.RotationSize + LeagueSimulator.BullpenSize));

        db.BeginBatch();
        try
        {
            for (int t = 0; t < TeamSeeds.Length; t++)
            {
                (string city, string name, string abbr, string division) = TeamSeeds[t];
                int teamId = t + 1;
                baseball.InsertTeam(new TeamRow
                {
                    TeamId = teamId,
                    City = city,
                    Name = name,
                    Abbreviation = abbr,
                    League = "DL",
                    Division = division,
                });

                // Frozen v3 prefix: 9 + 5 per team, main-stream draws only.
                for (int slot = 0; slot < LeagueSimulator.LineupSize + LeagueSimulator.RotationSize; slot++)
                {
                    bool isPitcher = slot >= LeagueSimulator.LineupSize;
                    (string id, int stuff) = GeneratePlayer(
                        players, baseball, teamId, isPitcher ? PitcherRole.Starter : PitcherRole.None,
                        ratingSpread, ref rng);
                    if (isPitcher)
                    {
                        pitchers.Add((id, stuff));
                    }
                }
            }

            // v4 pass — forked stream so the prefix above never moves.
            RngState v4Rng = rng.Split();
            for (int t = 0; t < TeamSeeds.Length; t++)
            {
                for (int slot = 0; slot < LeagueSimulator.BullpenSize; slot++)
                {
                    (string id, int stuff) = GeneratePlayer(
                        players, baseball, t + 1, PitcherRole.Reliever, ratingSpread, ref v4Rng);
                    pitchers.Add((id, stuff));
                }
            }
            foreach ((string id, int stuff) in pitchers)
            {
                GenerateArsenal(baseball, id, stuff, ratingSpread, ref v4Rng);
            }

            db.CommitBatch();
        }
        catch
        {
            db.RollbackBatch();
            throw;
        }
        return true;
    }

    /// <summary>
    /// v3→v4 save top-up: fills any team's bullpen to <see cref="LeagueSimulator.BullpenSize"/>
    /// relievers (Players + ratings + role + arsenal) in one batch. Migrated
    /// pitchers' role/arsenal rows come from the idempotent DDL backfill; only
    /// people have to be invented here. No-op (and no rng draws) on a
    /// post-v4 world. Call before the sims Initialize.
    /// </summary>
    public static bool EnsureV4(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng)
    {
        if (baseball.CountTeams() == 0)
        {
            return false; // no league yet — GenerateIfEmpty owns fresh worlds
        }

        var roster = new List<RosterPlayerRow>(
            LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam);
        baseball.LoadRoster(roster);

        Span<int> relievers = stackalloc int[LeagueSimulator.TeamCount + 1];
        foreach (RosterPlayerRow row in roster)
        {
            if (row.Role == PitcherRole.Reliever && row.TeamId >= 1 && row.TeamId <= LeagueSimulator.TeamCount)
            {
                relievers[row.TeamId]++;
            }
        }

        bool topUpNeeded = false;
        for (int teamId = 1; teamId <= LeagueSimulator.TeamCount; teamId++)
        {
            if (relievers[teamId] < LeagueSimulator.BullpenSize)
            {
                topUpNeeded = true;
            }
        }
        if (!topUpNeeded)
        {
            return false;
        }

        db.BeginBatch();
        try
        {
            for (int teamId = 1; teamId <= LeagueSimulator.TeamCount; teamId++)
            {
                for (int i = relievers[teamId]; i < LeagueSimulator.BullpenSize; i++)
                {
                    (string id, int stuff) = GeneratePlayer(
                        players, baseball, teamId, PitcherRole.Reliever, ratingSpread, ref rng);
                    GenerateArsenal(baseball, id, stuff, ratingSpread, ref rng);
                }
            }
            db.CommitBatch();
        }
        catch
        {
            db.RollbackBatch();
            throw;
        }
        return true;
    }

    private static (string PlayerId, int PitStuff) GeneratePlayer(
        PlayerQueries players, BaseballQueries baseball, int teamId, PitcherRole role,
        int ratingSpread, ref RngState rng)
    {
        string playerId = NextGuid(ref rng);
        players.Insert(new PlayerRow
        {
            PlayerId = playerId,
            FirstName = FirstNames[rng.NextInt(FirstNames.Length)],
            LastName = LastNames[rng.NextInt(LastNames.Length)],
            Age = 21 + rng.NextInt(16),
            TeamId = teamId,
            Funds = 0,
            HealthCeiling = 100,
            Recklessness = RollRating(ratingSpread, ref rng),
            BaseballInterest = 0,
            DetectionRisk = 0,
        });

        bool isPitcher = role != PitcherRole.None;
        int stuff = 0;
        var ratings = new PlayerRatingsRow
        {
            PlayerId = playerId,
            IsPitcher = isPitcher,
            BatPower = RollRating(ratingSpread, ref rng),
            BatContact = RollRating(ratingSpread, ref rng),
            BatDiscipline = RollRating(ratingSpread, ref rng),
            PitStuff = stuff = RollRating(ratingSpread, ref rng),
            PitControl = RollRating(ratingSpread, ref rng),
            PitStamina = RollRating(ratingSpread, ref rng),
            Fielding = RollRating(ratingSpread, ref rng),
        };
        if (role == PitcherRole.Reliever)
        {
            ratings.PitStamina = Math.Max(0, ratings.PitStamina - RelieverStaminaPenalty);
        }
        baseball.UpsertRatings(in ratings);

        if (isPitcher)
        {
            baseball.UpsertPitcherRole(playerId, role);
        }
        return (playerId, stuff);
    }

    /// <summary>
    /// Writes a varied three-pitch arsenal shaped by the pitcher's stuff — the
    /// same silhouette as the DDL migration backfill (fastball rides stuff,
    /// breaking carries movement, offspeed trails both, 60/25/15-ish mix) with
    /// per-pitch jitter. spread = 0 collapses the jitter so the §8 calibration
    /// roster stays perfectly uniform.
    /// </summary>
    public static void GenerateArsenal(
        BaseballQueries baseball, string playerId, int pitStuff, int ratingSpread, ref RngState rng)
    {
        static int Jitter(int halfWidth, int spread, ref RngState rng) =>
            spread == 0 || halfWidth == 0 ? 0 : rng.NextInt(2 * halfWidth + 1) - halfWidth;

        int fastballUsage = ratingSpread == 0 ? 60 : 45 + rng.NextInt(26);       // 45–70
        int breakingUsage = ratingSpread == 0 ? 25 : 15 + rng.NextInt(16);       // 15–30
        int offspeedUsage = 100 - fastballUsage - breakingUsage;                 // ≥ 0 by bounds

        baseball.UpsertArsenalPitch(new PitchArsenalRow
        {
            PlayerId = playerId,
            Type = PitchType.Fastball,
            Velocity = Math.Clamp(pitStuff + Jitter(10, ratingSpread, ref rng), 0, 100),
            Movement = Math.Clamp(40 + Jitter(10, ratingSpread, ref rng), 0, 100),
            UsageWeight = fastballUsage,
        });
        baseball.UpsertArsenalPitch(new PitchArsenalRow
        {
            PlayerId = playerId,
            Type = PitchType.Breaking,
            Velocity = Math.Clamp(pitStuff - 20 + Jitter(8, ratingSpread, ref rng), 0, 100),
            Movement = Math.Clamp(pitStuff + Jitter(10, ratingSpread, ref rng), 0, 100),
            UsageWeight = breakingUsage,
        });
        baseball.UpsertArsenalPitch(new PitchArsenalRow
        {
            PlayerId = playerId,
            Type = PitchType.Offspeed,
            Velocity = Math.Clamp(pitStuff - 25 + Jitter(8, ratingSpread, ref rng), 0, 100),
            Movement = Math.Clamp(pitStuff + 5 + Jitter(10, ratingSpread, ref rng), 0, 100),
            UsageWeight = offspeedUsage,
        });
    }

    private static string NextGuid(ref RngState rng)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, rng.NextUInt64());
        BitConverter.TryWriteBytes(bytes[8..], rng.NextUInt64());
        return new Guid(bytes).ToString();
    }

    /// <summary>Sum-of-three-uniforms bell around 50, clamped to the 0–100 CHECK bounds.</summary>
    private static int RollRating(int spread, ref RngState rng)
    {
        if (spread == 0)
        {
            return 50;
        }
        double bell = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) / 1.5;
        return Math.Clamp((int)Math.Round(50 + spread * bell), 0, 100);
    }
}
