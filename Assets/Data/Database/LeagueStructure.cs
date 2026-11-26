namespace DirtAndDiamonds.Data;

/// <summary>
/// One Appendix A team seed (league_restructure.md). <see cref="IsPlaceholder"/>
/// marks the names the spec flags *new*: a later data edit may rename them
/// (the TEAM.md team-names gate and the trademark screen,
/// steam_commercial_release.md §5).
/// </summary>
public readonly record struct TeamSeed(
    int TeamId, LeagueTier Tier, string City, string Name, string Abbreviation,
    int ConferenceId, int? DivisionId, bool IsPlaceholder);

/// <summary>
/// The static Phase 13 league structure (league_restructure.md §2 and
/// Appendix A): 3 tiers, 6 conferences, 14 divisions, 68 teams. Pure data with
/// no rng, so world-gen writes it verbatim and every save has the same shape.
///
/// Ids are readable and derived:
/// team ids are <c>(tier + 1) * 100 + n</c> (HS 101–112, College 201–224,
/// Pro 301–332, §2.2); conference ids are <c>(tier + 1) * 10 + n</c>
/// (HS 11–12, College 21–22, Pro 31–32); division ids are
/// <c>conference_id * 10 + n</c> (College 211–223, Pro 311–324). Team ids run
/// in Appendix A order: conference, then division, then the table's
/// left-to-right order.
///
/// It lives in the data layer (not in LeagueGenerator) so every harness that
/// compiles <c>Assets/Data/Database</c> can write schema-valid Teams rows.
/// </summary>
public static class LeagueStructure
{
    public static readonly ConferenceRow[] Conferences =
    {
        new(11, LeagueTier.HS, "North"),
        new(12, LeagueTier.HS, "South"),
        new(21, LeagueTier.College, "East"),
        new(22, LeagueTier.College, "West"),
        new(31, LeagueTier.Pro, "East"),
        new(32, LeagueTier.Pro, "West"),
    };

    public static readonly DivisionRow[] Divisions =
    {
        new(211, 21, "Coastal"),
        new(212, 21, "Capital"),
        new(213, 21, "Valley"),
        new(221, 22, "Lakes"),
        new(222, 22, "Frontier"),
        new(223, 22, "Canyon"),
        new(311, 31, "Northeast"),
        new(312, 31, "Atlantic"),
        new(313, 31, "Great Lakes"),
        new(314, 31, "Gulf"),
        new(321, 32, "Plains"),
        new(322, 32, "Mountain"),
        new(323, 32, "Southwest"),
        new(324, 32, "Pacific"),
    };

    /// <summary>
    /// All 68 Appendix A teams, ordered by team id (= world-gen draw order).
    /// Every one of the 40 shipped identities is kept: HS keeps its 8 schools,
    /// College its 8, and Pro takes the 8 former MLB and 24 former minor-league
    /// seeds (§2.3).
    /// </summary>
    public static readonly TeamSeed[] Teams =
    {
        // ---- High School (12): North 101–106, South 107–112; no divisions ----
        new(101, LeagueTier.HS, "Crestwood", "Cardinals", "CRW", 11, null, false),
        new(102, LeagueTier.HS, "Lakeside", "Longhorns", "LKS", 11, null, false),
        new(103, LeagueTier.HS, "Fairview", "Falcons", "FRV", 11, null, false),
        new(104, LeagueTier.HS, "Oak Ridge", "Owls", "OKR", 11, null, false),
        new(105, LeagueTier.HS, "Brookhaven", "Bulldogs", "BRK", 11, null, true),
        new(106, LeagueTier.HS, "Cedar Grove", "Comets", "CDG", 11, null, true),
        new(107, LeagueTier.HS, "Pinehurst", "Panthers", "PNH", 12, null, false),
        new(108, LeagueTier.HS, "Westfield", "Warhawks", "WSF", 12, null, false),
        new(109, LeagueTier.HS, "Summit", "Spartans", "SMT", 12, null, false),
        new(110, LeagueTier.HS, "Riverton", "Rams", "RVT", 12, null, false),
        new(111, LeagueTier.HS, "Granite Falls", "Gators", "GRF", 12, null, true),
        new(112, LeagueTier.HS, "Hillcrest", "Hurricanes", "HLC", 12, null, true),

        // ---- College (24): 2 conferences × 3 divisions × 4 ----
        new(201, LeagueTier.College, "Ashford State", "Admirals", "ASH", 21, 211, false),
        new(202, LeagueTier.College, "Blue Valley", "Bison", "BLV", 21, 211, false),
        new(203, LeagueTier.College, "Glenhaven", "Gulls", "GLH", 21, 211, true),
        new(204, LeagueTier.College, "Saltmarsh State", "Stingrays", "SMS", 21, 211, true),
        new(205, LeagueTier.College, "Carverton Tech", "Chargers", "CVT", 21, 212, false),
        new(206, LeagueTier.College, "Dunmore", "Dukes", "DNM", 21, 212, false),
        new(207, LeagueTier.College, "Marbury", "Miners", "MRB", 21, 212, true),
        new(208, LeagueTier.College, "Wexford", "Wildcats", "WXF", 21, 212, true),
        new(209, LeagueTier.College, "Lorain Valley", "Lancers", "LRV", 21, 213, true),
        new(210, LeagueTier.College, "Northgate", "Nighthawks", "NGT", 21, 213, true),
        new(211, LeagueTier.College, "Union Mills", "Millers", "UNM", 21, 213, true),
        new(212, LeagueTier.College, "Vance County", "Vipers", "VNC", 21, 213, true),
        new(213, LeagueTier.College, "Eastlake", "Eagles", "ELU", 22, 221, false),
        new(214, LeagueTier.College, "Midland", "Mavericks", "MAM", 22, 221, false),
        new(215, LeagueTier.College, "Kessler State", "Kodiaks", "KSS", 22, 221, true),
        new(216, LeagueTier.College, "York Plains", "Pioneers", "YKP", 22, 221, true),
        new(217, LeagueTier.College, "Fort Garland", "Grizzlies", "FTG", 22, 222, false),
        new(218, LeagueTier.College, "Hollis", "Hawks", "HOL", 22, 222, false),
        new(219, LeagueTier.College, "Oxbow A&M", "Otters", "OXB", 22, 222, true),
        new(220, LeagueTier.College, "Redstone Tech", "Rockets", "RST", 22, 222, true),
        new(221, LeagueTier.College, "Pell City", "Pilots", "PLC", 22, 223, true),
        new(222, LeagueTier.College, "Quarry Hill", "Quarrymen", "QRH", 22, 223, true),
        new(223, LeagueTier.College, "Tolland", "Timberwolves", "TOL", 22, 223, true),
        new(224, LeagueTier.College, "Zion Ridge", "Zephyrs", "ZNR", 22, 223, true),

        // ---- Pro (32): 2 conferences × 4 divisions × 4 ----
        new(301, LeagueTier.Pro, "Bayport", "Barons", "BAY", 31, 311, false),
        new(302, LeagueTier.Pro, "Kingsbridge", "Krakens", "KBR", 31, 311, false),
        new(303, LeagueTier.Pro, "Harborview", "Herons", "HBV", 31, 311, false),
        new(304, LeagueTier.Pro, "Scranton", "Steamers", "SCR", 31, 311, false),
        new(305, LeagueTier.Pro, "Charlotte", "Crowns", "CLT", 31, 312, false),
        new(306, LeagueTier.Pro, "Durham", "Drakes", "DRM", 31, 312, false),
        new(307, LeagueTier.Pro, "Roanoke", "Ridgerunners", "RNK", 31, 312, false),
        new(308, LeagueTier.Pro, "Palmetto", "Pelicans", "PLM", 31, 312, false),
        new(309, LeagueTier.Pro, "Ironvale", "Foxes", "IRV", 31, 313, false),
        new(310, LeagueTier.Pro, "Toledo", "Titans", "TLD", 31, 313, false),
        new(311, LeagueTier.Pro, "Galena", "Gliders", "GLN", 31, 313, false),
        new(312, LeagueTier.Pro, "Norwood", "Nailers", "NRW", 31, 313, false),
        new(313, LeagueTier.Pro, "Hattiesburg", "Hornets", "HTB", 31, 314, false),
        new(314, LeagueTier.Pro, "Chattahoochee", "Catfish", "CHT", 31, 314, false),
        new(315, LeagueTier.Pro, "Kannapolis", "Knights", "KNP", 31, 314, false),
        new(316, LeagueTier.Pro, "Laredo", "Lobos", "LRD", 31, 314, false),
        new(317, LeagueTier.Pro, "Omaha", "Outlaws", "OMA", 32, 321, false),
        new(318, LeagueTier.Pro, "Cedar Rapids", "Colts", "CDR", 32, 321, false),
        new(319, LeagueTier.Pro, "Sioux Bend", "Storm", "SXB", 32, 321, false),
        new(320, LeagueTier.Pro, "Twin Forks", "Trout", "TWF", 32, 321, false),
        new(321, LeagueTier.Pro, "Bozeman", "Bighorns", "BZM", 32, 322, false),
        new(322, LeagueTier.Pro, "Provo", "Prospectors", "PRV", 32, 322, false),
        new(323, LeagueTier.Pro, "Albuquerque", "Aviators", "ABQ", 32, 322, false),
        new(324, LeagueTier.Pro, "Dustfield", "Drifters", "DST", 32, 322, false),
        new(325, LeagueTier.Pro, "Mesa Roja", "Rattlers", "MRJ", 32, 323, false),
        new(326, LeagueTier.Pro, "Sablewood", "Stallions", "SBW", 32, 323, false),
        new(327, LeagueTier.Pro, "Fresno", "Firebirds", "FRS", 32, 323, false),
        new(328, LeagueTier.Pro, "Modesto", "Mustangs", "MDS", 32, 323, false),
        new(329, LeagueTier.Pro, "Silverlake", "Sharks", "SLK", 32, 324, false),
        new(330, LeagueTier.Pro, "Cascadia", "Wolves", "CAS", 32, 324, false),
        new(331, LeagueTier.Pro, "Tacoma", "Thunder", "TAC", 32, 324, false),
        new(332, LeagueTier.Pro, "Yuba City", "Yellowjackets", "YBC", 32, 324, false),
    };

    /// <summary>Teams per tier (§2): HS 12, College 24, Pro 32.</summary>
    public static int TeamCount(LeagueTier tier) => tier switch
    {
        LeagueTier.HS => 12,
        LeagueTier.College => 24,
        _ => 32,
    };

    /// <summary>First team_id of a tier minus one (§2.2): uniformly (tier + 1) * 100.</summary>
    public static int TeamIdBase(LeagueTier tier) => ((int)tier + 1) * 100;

    /// <summary>
    /// Teams.league display label (§2.2). "PRO" is a placeholder brand (a §16
    /// open item); "CBL" is pending name clearance.
    /// </summary>
    public static string LeagueLabel(LeagueTier tier) => tier switch
    {
        LeagueTier.HS => "HS",
        LeagueTier.College => "CBL",
        _ => "PRO",
    };

    /// <summary>
    /// The first conference of a tier: what a test fixture's ad-hoc team joins
    /// when it only needs a schema-valid row (Teams.conference_id is NOT NULL).
    /// </summary>
    public static int FirstConferenceId(LeagueTier tier) => ((int)tier + 1) * 10 + 1;

    /// <summary>Teams.division display text: the division's name, or the conference's for HS.</summary>
    public static string DivisionLabel(in TeamSeed seed)
    {
        if (seed.DivisionId is int divisionId)
        {
            foreach (DivisionRow d in Divisions)
            {
                if (d.DivisionId == divisionId)
                {
                    return d.Name;
                }
            }
        }
        foreach (ConferenceRow c in Conferences)
        {
            if (c.ConferenceId == seed.ConferenceId)
            {
                return c.Name;
            }
        }
        throw new InvalidOperationException($"Team {seed.TeamId} names an unknown conference {seed.ConferenceId}.");
    }

    /// <summary>The schema-valid Teams row for a seed and its home park (the world-gen writer's shape).</summary>
    public static TeamRow ToTeamRow(in TeamSeed seed, string homePark) => new()
    {
        TeamId = seed.TeamId,
        City = seed.City,
        Name = seed.Name,
        Abbreviation = seed.Abbreviation,
        League = LeagueLabel(seed.Tier),
        Division = DivisionLabel(seed),
        ConferenceId = seed.ConferenceId,
        DivisionId = seed.DivisionId,
        HomePark = homePark,
        Tier = seed.Tier,
    };

    /// <summary>
    /// PLACEHOLDER home park, used only when world-gen runs without the
    /// home_parks.json catalog (headless fixtures): "{City} Ballpark", or
    /// "{City} High Field" for HS. The game always passes the catalog, and the
    /// G-7 venue catalog replaces both later.
    /// </summary>
    public static string PlaceholderHomePark(in TeamSeed seed) =>
        seed.Tier == LeagueTier.HS ? seed.City + " High Field" : seed.City + " Ballpark";
}
