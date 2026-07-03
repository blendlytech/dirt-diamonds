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
    /// Seeds the full league (8 teams × 9 position players + 5 starters) in one
    /// batch transaction when Teams is empty. Returns false when a league
    /// already exists (normal boot on an existing save).
    /// </summary>
    public static bool GenerateIfEmpty(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        int ratingSpread, ref RngState rng)
    {
        if (baseball.CountTeams() > 0)
        {
            return false;
        }

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

                for (int slot = 0; slot < LeagueSimulator.RosterSizePerTeam; slot++)
                {
                    bool isPitcher = slot >= LeagueSimulator.LineupSize;
                    GeneratePlayer(players, baseball, teamId, isPitcher, ratingSpread, ref rng);
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

    private static void GeneratePlayer(
        PlayerQueries players, BaseballQueries baseball, int teamId, bool isPitcher,
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

        baseball.UpsertRatings(new PlayerRatingsRow
        {
            PlayerId = playerId,
            IsPitcher = isPitcher,
            BatPower = RollRating(ratingSpread, ref rng),
            BatContact = RollRating(ratingSpread, ref rng),
            BatDiscipline = RollRating(ratingSpread, ref rng),
            PitStuff = RollRating(ratingSpread, ref rng),
            PitControl = RollRating(ratingSpread, ref rng),
            PitStamina = RollRating(ratingSpread, ref rng),
            Fielding = RollRating(ratingSpread, ref rng),
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
