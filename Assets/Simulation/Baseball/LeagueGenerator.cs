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

    // Ladder-tier team seeds (Phase 9a). One 8-team league per tier below MLB;
    // MLB keeps TeamSeeds above (its ids and generation order are the frozen
    // M1 prefix). Indexed by (int)LeagueTier for tiers 0–4.
    private static readonly (string City, string Name, string Abbr, string Division)[][] TierTeamSeeds =
    {
        new[] // HS
        {
            ("Crestwood", "Cardinals", "CRW", "East"),
            ("Lakeside", "Longhorns", "LKS", "East"),
            ("Fairview", "Falcons", "FRV", "East"),
            ("Oak Ridge", "Owls", "OKR", "East"),
            ("Pinehurst", "Panthers", "PNH", "West"),
            ("Westfield", "Warhawks", "WSF", "West"),
            ("Summit", "Spartans", "SMT", "West"),
            ("Riverton", "Rams", "RVT", "West"),
        },
        new[] // College
        {
            ("Ashford State", "Admirals", "ASH", "East"),
            ("Blue Valley", "Bison", "BLV", "East"),
            ("Carverton Tech", "Chargers", "CVT", "East"),
            ("Dunmore", "Dukes", "DNM", "East"),
            ("Eastlake", "Eagles", "ELU", "West"),
            ("Fort Garland", "Grizzlies", "FTG", "West"),
            ("Hollis", "Hawks", "HOL", "West"),
            ("Midland", "Mavericks", "MAM", "West"),
        },
        new[] // MinorA
        {
            ("Cedar Rapids", "Colts", "CDR", "East"),
            ("Twin Forks", "Trout", "TWF", "East"),
            ("Palmetto", "Pelicans", "PLM", "East"),
            ("Bozeman", "Bighorns", "BZM", "East"),
            ("Yuba City", "Yellowjackets", "YBC", "West"),
            ("Galena", "Gliders", "GLN", "West"),
            ("Norwood", "Nailers", "NRW", "West"),
            ("Sablewood", "Stallions", "SBW", "West"),
        },
        new[] // MinorAA
        {
            ("Hattiesburg", "Hornets", "HTB", "East"),
            ("Roanoke", "Ridgerunners", "RNK", "East"),
            ("Laredo", "Lobos", "LRD", "East"),
            ("Chattahoochee", "Catfish", "CHT", "East"),
            ("Provo", "Prospectors", "PRV", "West"),
            ("Sioux Bend", "Storm", "SXB", "West"),
            ("Kannapolis", "Knights", "KNP", "West"),
            ("Modesto", "Mustangs", "MDS", "West"),
        },
        new[] // MinorAAA
        {
            ("Albuquerque", "Aviators", "ABQ", "East"),
            ("Toledo", "Titans", "TLD", "East"),
            ("Fresno", "Firebirds", "FRS", "East"),
            ("Omaha", "Outlaws", "OMA", "East"),
            ("Scranton", "Steamers", "SCR", "West"),
            ("Tacoma", "Thunder", "TAC", "West"),
            ("Charlotte", "Crowns", "CLT", "West"),
            ("Durham", "Drakes", "DRM", "West"),
        },
    };

    /// <summary>Teams.league label per tier (display flavor, not logic).</summary>
    private static readonly string[] TierLeagueLabels = { "HS", "CBL", "A", "AA", "AAA", "DL" };

    // Tier-appropriate generated ages: Age = min + NextInt(span). MLB keeps the
    // frozen 21 + NextInt(16); the ladder gets younger as it descends. NPCs do
    // not yet age OUT of a tier (promotion is 9c) — a disclosed 9a artifact.
    private static readonly (int Min, int Span)[] TierAgeRolls =
    {
        (15, 4),  // HS: 15–18
        (18, 4),  // College: 18–21
        (19, 6),  // MinorA: 19–24
        (20, 7),  // MinorAA: 20–26
        (21, 8),  // MinorAAA: 21–28
        (21, 16), // MLB: 21–36 — the frozen v3 roll, never change
    };

    /// <summary>
    /// First team_id of a tier's 8-team block minus one: MLB keeps the frozen
    /// ids 1–8; lower tiers sit in readable hundred-blocks (HS 101–108,
    /// College 201–208, … MinorAAA 501–508).
    /// </summary>
    internal static int TierTeamIdBase(LeagueTier tier) =>
        tier == LeagueTier.MLB ? 0 : ((int)tier + 1) * 100;

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
                // Schema v7: this league is the MLB rung. Writing the tier row
                // draws no rng, so the frozen M1 generation prefix is untouched.
                baseball.UpsertTeamTier(teamId, LeagueTier.MLB);

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

    /// <summary>
    /// Invents one player (Players + ratings + role row when a pitcher) on
    /// <paramref name="teamId"/>, joining the caller's open batch. Internal
    /// because it is a building block, not a lifecycle: world-gen and EnsureV4
    /// call it in bulk, and the succession handoff (heir mechanics §5.4) calls
    /// it for a replacement-level backfill filler when the free-agent pool has
    /// nobody of the vacated role. A pitcher still needs
    /// <see cref="GenerateArsenal"/> called after.
    /// </summary>
    /// <summary>
    /// v6→v7 world top-up (Phase 9a), the same migration-function pattern as
    /// <see cref="EnsureV4"/>: seeds every ladder tier below MLB that has no
    /// teams yet — one 8-team league per tier, each team fully staffed
    /// 9 + 5 + 3 with arsenals, tier-appropriate ages — in one batch. Runs on
    /// both migrated saves (which have only the backfilled-MLB league) and
    /// fresh worlds (right after <see cref="GenerateIfEmpty"/>). No-op, and no
    /// rng draws, once every tier is populated — so existing harness fixtures
    /// that never call this keep their MLB-only worlds byte-for-byte.
    ///
    /// The uniform 9+5+3 roster shape across HS→MLB is a deliberate, disclosed
    /// 9a simplification: LineupSize/RotationSize/BullpenSize are compile-time
    /// constants shared by both sims' array shapes.
    /// </summary>
    public static bool EnsureTierLeagues(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng)
    {
        if (baseball.CountTeams() == 0)
        {
            return false; // no world yet — GenerateIfEmpty owns fresh worlds
        }

        Span<bool> tierMissing = stackalloc bool[LeagueDirectory.TierCount];
        bool anyMissing = false;
        for (int t = 0; t < (int)LeagueTier.MLB; t++)
        {
            tierMissing[t] = baseball.CountTeamsInTier((LeagueTier)t) == 0;
            anyMissing |= tierMissing[t];
        }
        if (!anyMissing)
        {
            return false;
        }

        db.BeginBatch();
        try
        {
            for (int t = 0; t < (int)LeagueTier.MLB; t++)
            {
                if (!tierMissing[t])
                {
                    continue;
                }
                var tier = (LeagueTier)t;
                (int minAge, int ageSpan) = TierAgeRolls[t];
                var pitchers = new List<(string Id, int Stuff)>(
                    TierTeamSeeds[t].Length * (LeagueSimulator.RotationSize + LeagueSimulator.BullpenSize));

                for (int s = 0; s < TierTeamSeeds[t].Length; s++)
                {
                    (string city, string name, string abbr, string division) = TierTeamSeeds[t][s];
                    int teamId = TierTeamIdBase(tier) + s + 1;
                    baseball.InsertTeam(new TeamRow
                    {
                        TeamId = teamId,
                        City = city,
                        Name = name,
                        Abbreviation = abbr,
                        League = TierLeagueLabels[t],
                        Division = division,
                    });
                    baseball.UpsertTeamTier(teamId, tier);

                    for (int slot = 0; slot < LeagueSimulator.RosterSizePerTeam; slot++)
                    {
                        PitcherRole role =
                            slot < LeagueSimulator.LineupSize ? PitcherRole.None
                            : slot < LeagueSimulator.LineupSize + LeagueSimulator.RotationSize ? PitcherRole.Starter
                            : PitcherRole.Reliever;
                        (string id, int stuff) = GeneratePlayer(
                            players, baseball, teamId, role, ratingSpread, ref rng, minAge, ageSpan);
                        if (role != PitcherRole.None)
                        {
                            pitchers.Add((id, stuff));
                        }
                    }
                }
                foreach ((string id, int stuff) in pitchers)
                {
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

    /// <param name="minAge">Low end of the generated-age roll. The defaults are
    /// the frozen v3 roll (21 + NextInt(16)) — every pre-9a caller keeps them;
    /// EnsureTierLeagues passes the tier-appropriate window. The roll is one
    /// NextInt draw either way, so the draw sequence shape never changes.</param>
    /// <param name="ageSpan">Width of the generated-age roll.</param>
    internal static (string PlayerId, int PitStuff) GeneratePlayer(
        PlayerQueries players, BaseballQueries baseball, int teamId, PitcherRole role,
        int ratingSpread, ref RngState rng, int minAge = 21, int ageSpan = 16)
    {
        string playerId = NextGuid(ref rng);
        players.Insert(new PlayerRow
        {
            PlayerId = playerId,
            FirstName = FirstNames[rng.NextInt(FirstNames.Length)],
            LastName = LastNames[rng.NextInt(LastNames.Length)],
            Age = minAge + rng.NextInt(ageSpan),
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

    /// <summary>
    /// One draw from the same first-name pool world-gen uses — for callers
    /// that need a name without inserting a row (CareerManager's bus-driven
    /// conception names the newborn; the surname is the bloodline's).
    /// </summary>
    public static string GenerateFirstName(ref RngState rng) =>
        FirstNames[rng.NextInt(FirstNames.Length)];

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
