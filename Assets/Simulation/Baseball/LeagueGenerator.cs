using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One-time world-gen for the league: seeds the schema v14 world
/// (league_restructure.md §2, §5.3, Appendix A) when the save has no league
/// yet (new game). Deterministic under a fixed <see cref="RngState"/> seed: the
/// same seed gives byte-identical tables. Runs once per save — this is
/// load-time code, not simulation-loop code, so ordinary allocation is fine.
/// </summary>
public static class LeagueGenerator
{
    /// <summary>Bell-ish rating spread around 50 used for a fresh game world.
    /// 0 generates a perfectly average league (the §8 calibration roster).</summary>
    public const int DefaultRatingSpread = 25;

    /// <summary>
    /// Generated ages per tier (§5.3), indexed by (int)<see cref="LeagueTier"/>:
    /// Age = min + NextInt(span). HS 15–18 (consistent with SchoolGrade),
    /// College 18–22, Pro 21–36 (the frozen v3 MLB roll).
    /// </summary>
    private static readonly (int Min, int Span)[] TierAgeRolls =
    {
        (15, 4),  // HS: 15–18
        (18, 5),  // College: 18–22
        (21, 16), // Pro: 21–36
    };

    /// <summary>
    /// One tier's generated roster shape; each count is <c>min + NextInt(span)</c>,
    /// and a span of 1 is a fixed count that draws nothing.
    /// </summary>
    private readonly record struct RosterShape(
        int PositionMin, int PositionSpan, int StartersMin, int StartersSpan, int RelieversMin, int RelieversSpan);

    /// <summary>
    /// Per-tier generation targets (§5.1/§5.3, 13-1 brief §3.2.3), indexed by
    /// (int)<see cref="LeagueTier"/>. HS: 11–14 position players, 3–4 SP,
    /// 2–4 RP (as specified). College: 15 / 4 / 5 = 24, inside every College
    /// cap (≤ 17 / ≤ 4 / ≤ 5, org 26) with 2 org slots left for recruiting.
    /// Pro: exactly 15 / 4 / 5 = 24, the Pro org cap. Designations stay
    /// unwritten until 13-5.
    /// </summary>
    private static readonly RosterShape[] TierRosterShapes =
    {
        new(11, 4, 3, 2, 2, 3), // HS
        new(15, 1, 4, 1, 5, 1), // College
        new(15, 1, 4, 1, 5, 1), // Pro
    };

    // ---- Player_Fielding generation (schema v14, arcade_baseball_overhaul.md §8.1) ----
    // 13-1 owns one invariant: every team's position players cover all eight
    // field positions (the first eight roster slots take them in
    // FieldingPositions.Field order; slots are exchangeable, since every slot
    // rolls the same rating bell). Everything else below is a PROPOSAL,
    // Math-owned (GPT 6 Astra Pro, G-1/G-4): the depth weights, the secondary
    // rate and the neighbour table are data values to tune, never logic. The
    // pass draws from its own stream (GenerateIfEmpty), so tuning it never
    // moves a rating.

    /// <summary>
    /// PROPOSAL (Math-owned): primary-position weights for position players
    /// past the first eight. Catchers weigh most, so most rosters carry a
    /// backup; the premium up-the-middle spots (2B, SS, CF) come next.
    /// </summary>
    private static readonly (FieldingPosition Position, int Weight)[] DepthPositionWeights =
    {
        (FieldingPosition.C, 3),
        (FieldingPosition.FirstBase, 1),
        (FieldingPosition.SecondBase, 2),
        (FieldingPosition.ThirdBase, 1),
        (FieldingPosition.Shortstop, 2),
        (FieldingPosition.LeftField, 1),
        (FieldingPosition.CenterField, 2),
        (FieldingPosition.RightField, 1),
    };

    /// <summary>PROPOSAL (Math-owned): the share of position players who also list a secondary position.</summary>
    public const double SecondaryPositionRate = 0.35;

    /// <summary>
    /// PROPOSAL (Math-owned): the positions a player at each primary can
    /// credibly back up, indexed by (int)<see cref="FieldingPosition"/>. A
    /// secondary is drawn uniformly from the row. Pitchers list none.
    /// </summary>
    private static readonly FieldingPosition[][] SecondaryNeighbours =
    {
        Array.Empty<FieldingPosition>(),                                                                  // P
        new[] { FieldingPosition.FirstBase },                                                             // C
        new[] { FieldingPosition.ThirdBase, FieldingPosition.LeftField, FieldingPosition.RightField },    // 1B
        new[] { FieldingPosition.Shortstop, FieldingPosition.ThirdBase },                                 // 2B
        new[] { FieldingPosition.FirstBase, FieldingPosition.Shortstop, FieldingPosition.SecondBase },    // 3B
        new[] { FieldingPosition.SecondBase, FieldingPosition.ThirdBase },                                // SS
        new[] { FieldingPosition.RightField, FieldingPosition.CenterField, FieldingPosition.FirstBase },  // LF
        new[] { FieldingPosition.LeftField, FieldingPosition.RightField },                                // CF
        new[] { FieldingPosition.LeftField, FieldingPosition.CenterField, FieldingPosition.FirstBase },   // RF
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
    /// Seeds the schema v14 world in ONE batch transaction when Teams is empty
    /// (13-1 brief §4.4): the static conference and division rows, all 68
    /// Appendix A teams with their tier rows and home parks, then every team's
    /// roster (ratings, role, potential, person row) with a Player_Fielding row
    /// for every player, then every pitcher's arsenal. Returns false when a
    /// league already exists (normal boot on an existing save).
    ///
    /// <para><b>Determinism.</b> A pure function of <paramref name="rng"/>'s
    /// seed. Draw order is fixed: two sub-stream seeds first, then tier HS →
    /// College → Pro, team id ascending (= <see cref="LeagueStructure.Teams"/>
    /// order), then roster slot (position players, starters, relievers), with
    /// each team's three count rolls before its players. Each player keeps the
    /// per-player <c>Split()</c> fork for the prospect discount and person
    /// stats (the HS-2 pattern), so person stats never shift a rating draw. The
    /// fielding and arsenal passes draw from their own seeded streams, so a
    /// Math retune of either never moves a rating either.</para>
    /// </summary>
    /// <param name="homeParks">team_id → park name, from home_parks.json
    /// (<c>HomeParkCatalog.ToHomeParkMap</c>), which must cover every team.
    /// Null writes <see cref="LeagueStructure.PlaceholderHomePark"/> names
    /// (headless fixtures only; the game always passes the catalog).</param>
    public static bool GenerateIfEmpty(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng, PersonQueries? persons = null,
        IReadOnlyDictionary<int, string>? homeParks = null)
    {
        if (baseball.CountTeams() > 0)
        {
            return false;
        }

        // Sub-streams for the two Math-tunable passes, seeded from the first
        // two main-stream draws (never a Split() of the main stream, whose
        // long-jump would overlap the per-player discount forks).
        var fieldingRng = new RngState(rng.NextUInt64() | 1UL);
        var arsenalRng = new RngState(rng.NextUInt64() | 1UL);

        // (id, stuff) of every pitcher in generation order, for the arsenal pass.
        var pitchers = new List<(string Id, int Stuff)>(LeagueStructure.Teams.Length * 9);
        var teamPositionPlayers = new List<string>(TierRosterShapes[0].PositionMin + TierRosterShapes[0].PositionSpan);
        var teamPitchers = new List<string>(9);

        db.BeginBatch();
        try
        {
            // Structure first: conferences, divisions, teams, tier rows. No rng.
            baseball.EnsureLeagueStructure();
            foreach (TeamSeed seed in LeagueStructure.Teams)
            {
                baseball.InsertTeam(LeagueStructure.ToTeamRow(seed, HomeParkFor(seed, homeParks)));
                baseball.UpsertTeamTier(seed.TeamId, seed.Tier);
            }

            // Rosters, in the fixed draw order.
            foreach (TeamSeed seed in LeagueStructure.Teams)
            {
                RosterShape shape = TierRosterShapes[(int)seed.Tier];
                (int minAge, int ageSpan) = TierAgeRolls[(int)seed.Tier];
                int positionCount = RollCount(shape.PositionMin, shape.PositionSpan, ref rng);
                int starterCount = RollCount(shape.StartersMin, shape.StartersSpan, ref rng);
                int relieverCount = RollCount(shape.RelieversMin, shape.RelieversSpan, ref rng);

                teamPositionPlayers.Clear();
                teamPitchers.Clear();
                for (int i = 0; i < positionCount; i++)
                {
                    (string id, _) = GeneratePlayer(
                        players, baseball, seed.TeamId, PitcherRole.None, ratingSpread, ref rng, minAge, ageSpan, persons);
                    teamPositionPlayers.Add(id);
                }
                for (int i = 0; i < starterCount + relieverCount; i++)
                {
                    PitcherRole role = i < starterCount ? PitcherRole.Starter : PitcherRole.Reliever;
                    (string id, int stuff) = GeneratePlayer(
                        players, baseball, seed.TeamId, role, ratingSpread, ref rng, minAge, ageSpan, persons);
                    teamPitchers.Add(id);
                    pitchers.Add((id, stuff));
                }
                GenerateTeamFielding(baseball, teamPositionPlayers, teamPitchers, ratingSpread, ref fieldingRng);
            }

            foreach ((string id, int stuff) in pitchers)
            {
                GenerateArsenal(baseball, id, stuff, ratingSpread, ref arsenalRng);
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

    private static string HomeParkFor(in TeamSeed seed, IReadOnlyDictionary<int, string>? homeParks)
    {
        if (homeParks is null)
        {
            return LeagueStructure.PlaceholderHomePark(seed);
        }
        return homeParks.TryGetValue(seed.TeamId, out string? park)
            ? park
            : throw new InvalidOperationException(
                $"The home park catalog has no row for team {seed.TeamId} ({seed.Abbreviation}).");
    }

    private static int RollCount(int min, int span, ref RngState rng) =>
        span <= 1 ? min : min + rng.NextInt(span);

    /// <summary>
    /// Writes one team's Player_Fielding rows: position players in roster-slot
    /// order (the first eight cover the eight field positions; the rest draw a
    /// primary from <see cref="DepthPositionWeights"/>; each may roll a
    /// secondary), then its pitchers (<see cref="FieldingPosition.P"/>, no
    /// secondary). Every row rolls speed then arm on the shared rating bell.
    /// Joins the caller's open batch.
    /// </summary>
    private static void GenerateTeamFielding(
        BaseballQueries baseball, List<string> positionPlayers, List<string> pitchers,
        int ratingSpread, ref RngState rng)
    {
        if (positionPlayers.Count < FieldingPositions.Field.Length)
        {
            throw new InvalidOperationException(
                $"A team needs at least {FieldingPositions.Field.Length} position players to cover the field; it has {positionPlayers.Count}.");
        }

        for (int slot = 0; slot < positionPlayers.Count; slot++)
        {
            FieldingPosition primary = slot < FieldingPositions.Field.Length
                ? FieldingPositions.Field[slot]
                : DrawDepthPosition(ref rng);
            FieldingPosition? secondary = null;
            if (rng.NextDouble() < SecondaryPositionRate)
            {
                FieldingPosition[] neighbours = SecondaryNeighbours[(int)primary];
                secondary = neighbours[rng.NextInt(neighbours.Length)];
            }
            baseball.UpsertFielding(new PlayerFieldingRow
            {
                PlayerId = positionPlayers[slot],
                DefensivePosition = primary,
                SecondaryPosition = secondary,
                Speed = RollRating(ratingSpread, ref rng),
                Arm = RollRating(ratingSpread, ref rng),
            });
        }

        foreach (string pitcherId in pitchers)
        {
            baseball.UpsertFielding(new PlayerFieldingRow
            {
                PlayerId = pitcherId,
                DefensivePosition = FieldingPosition.P,
                SecondaryPosition = null,
                Speed = RollRating(ratingSpread, ref rng),
                Arm = RollRating(ratingSpread, ref rng),
            });
        }
    }

    private static FieldingPosition DrawDepthPosition(ref RngState rng)
    {
        int total = 0;
        foreach ((_, int weight) in DepthPositionWeights)
        {
            total += weight;
        }
        int roll = rng.NextInt(total);
        foreach ((FieldingPosition position, int weight) in DepthPositionWeights)
        {
            if (roll < weight)
            {
                return position;
            }
            roll -= weight;
        }
        throw new InvalidOperationException("Depth-position weights are empty.");
    }

    /// <summary>
    /// Invents one player (Players + ratings + role row when a pitcher) on
    /// <paramref name="teamId"/>, joining the caller's open batch. Internal
    /// because it is a building block, not a lifecycle: world-gen and the
    /// promotion pass's HS intake call it in bulk, and the succession handoff
    /// (heir mechanics §5.4) calls
    /// it for a replacement-level backfill filler when the free-agent pool has
    /// nobody of the vacated role. A pitcher still needs
    /// <see cref="GenerateArsenal"/> called after. It writes no Player_Fielding
    /// row: world-gen writes those per team (the all-eight-positions invariant
    /// is a team property); intake and fillers get theirs in 13-6/G-4.
    /// </summary>
    /// <param name="minAge">Low end of the generated-age roll. The defaults are
    /// the frozen v3 roll (21 + NextInt(16)); world-gen passes each tier's §5.3
    /// window. The roll is one NextInt draw either way, so the draw sequence
    /// shape never changes.</param>
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
