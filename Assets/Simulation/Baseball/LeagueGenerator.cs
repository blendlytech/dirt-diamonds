using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One-time world-gen: seeds Teams, Players and Player_Ratings when the save
/// has no league yet. Deterministic under a fixed <see cref="RngState"/> seed.
/// Runs once per save — load-time code, not simulation-loop code, so ordinary
/// allocation is fine.
///
/// Two worlds live here (schema v14, league_restructure.md §15):
/// <list type="bullet">
/// <item><see cref="GenerateWorld"/> — the shipped v14 world: Appendix A's 68
/// teams in 3 tiers with conferences, divisions and per-tier rosters (§5.3).
/// The game boots this.</item>
/// <item>The <b>legacy fixture ladder</b> — <see cref="GenerateIfEmpty"/>,
/// <see cref="EnsureV4"/>, <see cref="EnsureTierLeagues"/>: 8 teams per tier,
/// 9 + 5 + 3, with the frozen draw order. The calibration harness and the
/// season-pipeline guard run on it until slice 13-3 retires that guard; its
/// teams map onto their Appendix A conference/division by abbreviation, so
/// every row is schema-valid v14.</item>
/// </list>
/// </summary>
public static class LeagueGenerator
{
    /// <summary>Bell-ish rating spread around 50 used for a fresh game world.
    /// 0 generates a perfectly average league (the §8 calibration roster).</summary>
    public const int DefaultRatingSpread = 25;

    // ---- legacy fixture ladder (frozen; retired with the season guard in 13-3) ----

    // The frozen M1 Pro league: generation order and ids 1–8 are the season-
    // pipeline guard's prefix. Names, conference and division come from the
    // Appendix A seed with the same abbreviation (LeagueStructure).
    private static readonly string[] LegacyProAbbreviations =
        { "BAY", "KBR", "HBV", "IRV", "DST", "MRJ", "SLK", "CAS" };

    // The Phase 9a 8-team ladder leagues below Pro, indexed by (int)LeagueTier
    // (HS, College). Ids are TeamIdBase(tier) + slot (101–108, 201–208).
    private static readonly string[][] LegacyTierAbbreviations =
    {
        new[] { "CRW", "LKS", "FRV", "OKR", "PNH", "WSF", "SMT", "RVT" }, // HS
        new[] { "ASH", "BLV", "CVT", "DNM", "ELU", "FTG", "HOL", "MAM" }, // College
    };

    // The legacy ladder's frozen age rolls: Age = min + NextInt(span).
    private static readonly (int Min, int Span)[] LegacyAgeRolls =
    {
        (15, 4),  // HS: 15–18
        (18, 4),  // College: 18–21
        (21, 16), // Pro: 21–36 — the frozen v3 roll, never change
    };

    // ---- the v14 world (league_restructure.md §5.3) ----

    /// <summary>Generated age rolls per tier (§5.3): HS 15–18, College 18–22, Pro 21–36.</summary>
    private static readonly (int Min, int Span)[] WorldAgeRolls =
    {
        (15, 4),
        (18, 5),
        (21, 16),
    };

    /// <summary>
    /// One tier's generated roster shape, each count rolled as
    /// <c>min + NextInt(span)</c> (span 1 = fixed). §5.1/§5.3: HS 3–4 SP,
    /// 2–4 RP, 11–14 position players; College and Pro 4 SP, 5 RP and 15
    /// position players (Pro's exact org shape; College's is the §16-I6
    /// interpretation, 24 under its 26 cap).
    /// </summary>
    private readonly record struct RosterShape(
        int StartersMin, int StartersSpan, int RelieversMin, int RelieversSpan, int PositionMin, int PositionSpan);

    private static readonly RosterShape[] WorldRosterShapes =
    {
        new(3, 2, 2, 3, 11, 4), // HS
        new(4, 1, 5, 1, 15, 1), // College
        new(4, 1, 5, 1, 15, 1), // Pro
    };

    /// <summary>First team_id of a tier minus one (§2.2): uniformly (tier + 1) * 100.</summary>
    internal static int TierTeamIdBase(LeagueTier tier) => LeagueStructure.TeamIdBase(tier);

    private static TeamRow LegacyTeamRow(string abbreviation, int teamId)
    {
        if (!LeagueStructure.TryGetSeed(abbreviation, out TeamSeed seed))
        {
            throw new InvalidOperationException($"Legacy team {abbreviation} has no Appendix A seed.");
        }
        return LeagueStructure.ToTeamRow(seed, teamId);
    }

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
    /// Legacy fixture ladder, Pro rung: seeds the frozen 8-team Pro league
    /// (9 position players + 5 starters + 3 relievers per team, ids 1–8) in
    /// one batch transaction when Teams is empty. The game boots
    /// <see cref="GenerateWorld"/> instead; this is the calibration world.
    /// Returns false when a league already exists (normal boot on an existing
    /// save). The 9+5 loop draws exactly the v3 sequence from the caller's
    /// stream — every M1 calibration line is byte-for-byte reproducible — and
    /// all v4 additions (relievers, arsenals) draw from a Split() fork.
    /// </summary>
    public static bool GenerateIfEmpty(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng, PersonQueries? persons = null)
    {
        if (baseball.CountTeams() > 0)
        {
            return false;
        }

        // (id, stuff) of every pitcher in generation order, for the arsenal pass.
        var pitchers = new List<(string Id, int Stuff)>(
            LegacyProAbbreviations.Length * (LeagueSimulator.RotationSize + LeagueSimulator.BullpenSize));

        db.BeginBatch();
        try
        {
            // Schema v14 structure rows draw no rng, so the frozen M1
            // generation prefix below is untouched.
            baseball.EnsureLeagueStructure();
            for (int t = 0; t < LegacyProAbbreviations.Length; t++)
            {
                int teamId = t + 1;
                baseball.InsertTeam(LegacyTeamRow(LegacyProAbbreviations[t], teamId));
                // This league is the Pro rung (the former MLB). Writing the
                // tier row draws no rng either.
                baseball.UpsertTeamTier(teamId, LeagueTier.Pro);

                // Frozen v3 prefix: 9 + 5 per team, main-stream draws only.
                for (int slot = 0; slot < LeagueSimulator.LineupSize + LeagueSimulator.RotationSize; slot++)
                {
                    bool isPitcher = slot >= LeagueSimulator.LineupSize;
                    (string id, int stuff) = GeneratePlayer(
                        players, baseball, teamId, isPitcher ? PitcherRole.Starter : PitcherRole.None,
                        ratingSpread, ref rng, persons: persons);
                    if (isPitcher)
                    {
                        pitchers.Add((id, stuff));
                    }
                }
            }

            // v4 pass — forked stream so the prefix above never moves.
            RngState v4Rng = rng.Split();
            for (int t = 0; t < LegacyProAbbreviations.Length; t++)
            {
                for (int slot = 0; slot < LeagueSimulator.BullpenSize; slot++)
                {
                    (string id, int stuff) = GeneratePlayer(
                        players, baseball, t + 1, PitcherRole.Reliever, ratingSpread, ref v4Rng, persons: persons);
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
        int ratingSpread, ref RngState rng, PersonQueries? persons = null)
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
                        players, baseball, teamId, PitcherRole.Reliever, ratingSpread, ref rng, persons: persons);
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
    /// Legacy fixture ladder, lower rungs (Phase 9a), the same top-up pattern
    /// as <see cref="EnsureV4"/>: seeds every tier below Pro that has no teams
    /// yet — one 8-team league per tier (HS, College), each team fully staffed
    /// 9 + 5 + 3 with arsenals, tier-appropriate ages — in one batch, right
    /// after <see cref="GenerateIfEmpty"/>. No-op, and no rng draws, once every
    /// tier is populated — so harness fixtures that never call this keep their
    /// Pro-only worlds byte-for-byte. (Schema v14 dropped the three minor-league
    /// rungs; HS and College draw first, so their rosters are unchanged.)
    ///
    /// The uniform 9+5+3 roster shape across HS→MLB is a deliberate, disclosed
    /// 9a simplification: LineupSize/RotationSize/BullpenSize are compile-time
    /// constants shared by both sims' array shapes.
    /// </summary>
    public static bool EnsureTierLeagues(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng, PersonQueries? persons = null)
    {
        if (baseball.CountTeams() == 0)
        {
            return false; // no world yet — GenerateIfEmpty owns fresh worlds
        }

        Span<bool> tierMissing = stackalloc bool[LeagueDirectory.TierCount];
        bool anyMissing = false;
        for (int t = 0; t < (int)LeagueTier.Pro; t++)
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
            baseball.EnsureLeagueStructure();
            for (int t = 0; t < (int)LeagueTier.Pro; t++)
            {
                if (!tierMissing[t])
                {
                    continue;
                }
                var tier = (LeagueTier)t;
                (int minAge, int ageSpan) = LegacyAgeRolls[t];
                string[] abbreviations = LegacyTierAbbreviations[t];
                var pitchers = new List<(string Id, int Stuff)>(
                    abbreviations.Length * (LeagueSimulator.RotationSize + LeagueSimulator.BullpenSize));

                for (int s = 0; s < abbreviations.Length; s++)
                {
                    int teamId = TierTeamIdBase(tier) + s + 1;
                    baseball.InsertTeam(LegacyTeamRow(abbreviations[s], teamId));
                    baseball.UpsertTeamTier(teamId, tier);

                    for (int slot = 0; slot < LeagueSimulator.RosterSizePerTeam; slot++)
                    {
                        PitcherRole role =
                            slot < LeagueSimulator.LineupSize ? PitcherRole.None
                            : slot < LeagueSimulator.LineupSize + LeagueSimulator.RotationSize ? PitcherRole.Starter
                            : PitcherRole.Reliever;
                        (string id, int stuff) = GeneratePlayer(
                            players, baseball, teamId, role, ratingSpread, ref rng, minAge, ageSpan, persons);
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

    /// <summary>
    /// Seeds the v14 world (league_restructure.md §2, §5.3, Appendix A) in one
    /// batch when Teams is empty: the static conference/division rows, all 68
    /// Appendix A teams with their tier rows, and each team's roster in
    /// <see cref="WorldRosterShapes"/> order (position players, then starters,
    /// then relievers), followed by every pitcher's arsenal. Returns false when
    /// a league already exists. One stream, team-id order, so a seed fully
    /// determines the world.
    /// </summary>
    public static bool GenerateWorld(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng, PersonQueries? persons = null)
    {
        if (baseball.CountTeams() > 0)
        {
            return false;
        }

        var pitchers = new List<(string Id, int Stuff)>(LeagueStructure.Teams.Length * 9);
        db.BeginBatch();
        try
        {
            baseball.EnsureLeagueStructure();
            foreach (TeamSeed seed in LeagueStructure.Teams)
            {
                baseball.InsertTeam(LeagueStructure.ToTeamRow(seed));
                baseball.UpsertTeamTier(seed.TeamId, seed.Tier);

                RosterShape shape = WorldRosterShapes[(int)seed.Tier];
                (int minAge, int ageSpan) = WorldAgeRolls[(int)seed.Tier];
                int positionPlayers = shape.PositionMin + rng.NextInt(shape.PositionSpan);
                int starters = shape.StartersMin + rng.NextInt(shape.StartersSpan);
                int relievers = shape.RelieversMin + rng.NextInt(shape.RelieversSpan);

                for (int i = 0; i < positionPlayers; i++)
                {
                    GeneratePlayer(players, baseball, seed.TeamId, PitcherRole.None,
                        ratingSpread, ref rng, minAge, ageSpan, persons);
                }
                for (int i = 0; i < starters + relievers; i++)
                {
                    PitcherRole role = i < starters ? PitcherRole.Starter : PitcherRole.Reliever;
                    pitchers.Add(GeneratePlayer(players, baseball, seed.TeamId, role,
                        ratingSpread, ref rng, minAge, ageSpan, persons));
                }
            }
            foreach ((string id, int stuff) in pitchers)
            {
                GenerateArsenal(baseball, id, stuff, ratingSpread, ref rng);
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
    /// <param name="persons">HS-2 (person-layer doc §2.5): when wired AND the
    /// world rolls a real spread, the player also gets a modest person-stat
    /// row drawn from the SAME per-player Split() fork the prospect discount
    /// uses (after its seven draws) — the main stream never moves. Null (every
    /// pre-HS-2 caller) or spread 0 (calibration/guard worlds) writes nothing:
    /// absent rows read as neutral, which is exactly the §2.5 guard-world
    /// contract.</param>
    internal static (string PlayerId, int PitStuff) GeneratePlayer(
        PlayerQueries players, BaseballQueries baseball, int teamId, PitcherRole role,
        int ratingSpread, ref RngState rng, int minAge = 21, int ageSpan = 16,
        PersonQueries? persons = null)
    {
        string playerId = NextGuid(ref rng);
        // Locals preserve the frozen pre-v10 draw ORDER exactly (guid, first,
        // last, age, recklessness, then the seven rating rolls) — the v10
        // additions below must not permute the main stream's assignments.
        string firstName = FirstNames[rng.NextInt(FirstNames.Length)];
        string lastName = LastNames[rng.NextInt(LastNames.Length)];
        int age = minAge + rng.NextInt(ageSpan);
        players.Insert(new PlayerRow
        {
            PlayerId = playerId,
            FirstName = firstName,
            LastName = lastName,
            Age = age,
            TeamId = teamId,
            Funds = 0,
            HealthCeiling = 100,
            Recklessness = RollRating(ratingSpread, ref rng),
            BaseballInterest = 0,
            DetectionRisk = 0,
        });

        // Schema v10 (development doc §3.3): the rolled vector IS the player's
        // POTENTIAL — the ceiling he develops toward — deliberately consuming
        // the exact draws the pre-v10 code spent on current ratings, so the
        // frozen M1 generation prefix never moves. Current ratings sit a
        // youth-scaled prospect discount below it (raw now / projectable
        // later), drawn from a Split() fork (the v4-reliever precedent: the
        // parent stream is untouched). A 27+-year-old's discount is zero and
        // spread 0 collapses the whole thing (potential = current = 50), so
        // calibration rosters stay perfectly uniform.
        bool isPitcher = role != PitcherRole.None;
        var potential = new PlayerPotentialRow
        {
            PlayerId = playerId,
            BatPower = RollRating(ratingSpread, ref rng),
            BatContact = RollRating(ratingSpread, ref rng),
            BatDiscipline = RollRating(ratingSpread, ref rng),
            PitStuff = RollRating(ratingSpread, ref rng),
            PitControl = RollRating(ratingSpread, ref rng),
            PitStamina = RollRating(ratingSpread, ref rng),
            Fielding = RollRating(ratingSpread, ref rng),
        };
        if (role == PitcherRole.Reliever)
        {
            // The bullpen stamina penalty caps the CEILING too — a reliever
            // must not develop back into starter stamina.
            potential.PitStamina = Math.Max(0, potential.PitStamina - RelieverStaminaPenalty);
        }

        var ratings = new PlayerRatingsRow { PlayerId = playerId, IsPitcher = isPitcher };
        if (ratingSpread == 0)
        {
            ratings.BatPower = potential.BatPower;
            ratings.BatContact = potential.BatContact;
            ratings.BatDiscipline = potential.BatDiscipline;
            ratings.PitStuff = potential.PitStuff;
            ratings.PitControl = potential.PitControl;
            ratings.PitStamina = potential.PitStamina;
            ratings.Fielding = potential.Fielding;
        }
        else
        {
            RngState discountRng = rng.Split();
            ratings.BatPower = DevelopmentCurve.RawCurrent(potential.BatPower, age, ref discountRng);
            ratings.BatContact = DevelopmentCurve.RawCurrent(potential.BatContact, age, ref discountRng);
            ratings.BatDiscipline = DevelopmentCurve.RawCurrent(potential.BatDiscipline, age, ref discountRng);
            ratings.PitStuff = DevelopmentCurve.RawCurrent(potential.PitStuff, age, ref discountRng);
            ratings.PitControl = DevelopmentCurve.RawCurrent(potential.PitControl, age, ref discountRng);
            ratings.PitStamina = DevelopmentCurve.RawCurrent(potential.PitStamina, age, ref discountRng);
            ratings.Fielding = DevelopmentCurve.RawCurrent(potential.Fielding, age, ref discountRng);
            if (persons is not null)
            {
                // §2.5 organic person spread, appended to the already-forked
                // discount stream AFTER its seven draws: the seven RawCurrent
                // results above and every main-stream draw stay byte-identical
                // whether or not persons is wired.
                persons.Upsert(RollPersonRow(playerId, ref discountRng));
            }
        }
        baseball.UpsertRatings(in ratings);
        baseball.UpsertPotential(in potential);

        if (isPitcher)
        {
            baseball.UpsertPitcherRole(playerId, role);
        }
        // Arsenals are shaped by what the pitcher throws NOW, not his ceiling.
        return (playerId, ratings.PitStuff);
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

    /// <summary>
    /// §2.5 organic-NPC person spread: deliberately narrower than
    /// <see cref="DefaultRatingSpread"/> (25) — the universe isn't uniformly
    /// average, but person stats stay near the neutral 50 the HS-6 zero-at-50
    /// modifier is anchored on. Narratively live, calibration-inert: nothing
    /// reads these into a rating before HS-6.
    /// </summary>
    public const int PersonSpread = 15;

    /// <summary>Bell scale of the generated GPA around the 2.5 neutral, clamped to the schema's [0,4].</summary>
    public const double GpaSpread = 0.8;

    /// <summary>
    /// One organic person row: the twelve INTEGER stats in Player_Person
    /// column order, then the GPA bell — the draw order is part of the
    /// determinism contract, same as GeneratePlayer's own locals.
    /// </summary>
    private static PersonRow RollPersonRow(string playerId, ref RngState rng)
    {
        PersonRow row = PersonRow.Neutral(playerId);
        row.Intelligence = RollRating(PersonSpread, ref rng);
        row.Maturity = RollRating(PersonSpread, ref rng);
        row.Happiness = RollRating(PersonSpread, ref rng);
        row.Charisma = RollRating(PersonSpread, ref rng);
        row.Confidence = RollRating(PersonSpread, ref rng);
        row.Reputation = RollRating(PersonSpread, ref rng);
        row.SocialStatus = RollRating(PersonSpread, ref rng);
        row.Attractiveness = RollRating(PersonSpread, ref rng);
        row.Teamwork = RollRating(PersonSpread, ref rng);
        row.Morality = RollRating(PersonSpread, ref rng);
        row.Discipline = RollRating(PersonSpread, ref rng);
        row.WorkEthic = RollRating(PersonSpread, ref rng);
        double gpaBell = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) / 1.5;
        row.Gpa = Math.Clamp(2.5 + GpaSpread * gpaBell, 0.0, 4.0);
        return row;
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
