using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Platform.Steam;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// ----------------------------------------------------------------------
// Slice 13-7 (docs/tasks/13-7_ripple_brief.md §5): the Phase 13 ripple.
//   - SteamRules: the pure tier-token, status-token and tier-achievement
//     decisions (Assets/Platform/Steam/SteamRules.cs is linked into this
//     harness because the Facepunch-facing Steam files cannot be compiled
//     here), plus a source scan that the retired wire ids are gone and that
//     the two Steam classes really call SteamRules.
//   - PlayoffManager.GetStandings against the selection seeds and against
//     Team_Seasons, a forfeit included.
// Every check name carries "13-7" so the deltas this slice adds to the default
// MC log are removable by one rule (see the handoff's reconstruction recipe).
// `--only ripple`.
// ----------------------------------------------------------------------
internal static partial class Program
{
    /// <summary>First season year of the 13-7 fixtures (clear of the 13-4 fixtures, which stop below +120).</summary>
    private const int Slice137BaseYear = Slice134BaseYear + 200;

    private static void RunRippleSuite(string schemaPath, string repoRoot)
    {
        Console.WriteLine("--- 13-7 ripple (SteamRules, retired wire ids, the GetStandings view) ---");
        RunSteamRulesChecks(repoRoot);
        RunStandingsViewChecks(schemaPath);
    }

    // ------------------------------------------------------------------
    // SteamRules + source scans
    // ------------------------------------------------------------------

    private static void RunSteamRulesChecks(string repoRoot)
    {
        Check("13-7 SteamRules (D2-A): HS / College / Pro publish #Tier_HS / #Tier_College / #Tier_Pro, all distinct",
            SteamRules.TierToken(LeagueTier.HS) == "#Tier_HS"
            && SteamRules.TierToken(LeagueTier.College) == "#Tier_College"
            && SteamRules.TierToken(LeagueTier.Pro) == "#Tier_Pro");

        string career = SteamRules.StatusToken(hasTeam: true, lineageOver: false);
        string family = SteamRules.StatusToken(hasTeam: false, lineageOver: false);
        string between = SteamRules.StatusToken(hasTeam: true, lineageOver: true);
        string betweenNoTeam = SteamRules.StatusToken(hasTeam: false, lineageOver: true);
        Check("13-7 SteamRules (D3-A): rostered = #Status_Career; unrostered with a live bloodline = #Status_Family; a finished lineage = #Status_BetweenCareers either way",
            career == "#Status_Career" && family == "#Status_Family"
            && between == "#Status_BetweenCareers" && betweenNoTeam == "#Status_BetweenCareers",
            $"{career} / {family} / {between} / {betweenNoTeam}");

        var perTier = new List<string>();
        bool oneForPro = true;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tier = (LeagueTier)t;
            ReadOnlySpan<string> ids = SteamRules.TierAchievements(tier);
            perTier.Add($"{tier}:{ids.Length}");
            oneForPro &= tier == LeagueTier.Pro
                ? ids.Length == 1 && ids[0] == "ACH_THE_SHOW"
                : ids.Length == 0;
        }
        Check("13-7 SteamRules (D1-A): reaching Pro unlocks exactly one achievement (ACH_THE_SHOW); HS and College unlock none",
            oneForPro, string.Join(" ", perTier));

        // Source scans: the Facepunch-facing classes can't be linked here, so prove by text.
        string steamDir = Path.Combine(repoRoot, "Assets", "Platform", "Steam");
        string[] retired = { "#Tier_MLB", "#Tier_Minor", "ACH_WENT_PRO" };
        var hits = new List<string>();
        int scanned = 0;
        foreach (string file in Directory.GetFiles(steamDir, "*.cs"))
        {
            scanned++;
            string text = File.ReadAllText(file);
            foreach (string token in retired)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    hits.Add($"{Path.GetFileName(file)}: {token}");
                }
            }
        }
        Check("13-7 source scan (D1/D2): no Assets/Platform/Steam/*.cs names #Tier_MLB, #Tier_Minor* or ACH_WENT_PRO",
            scanned >= 4 && hits.Count == 0, hits.Count == 0 ? $"{scanned} files" : string.Join(", ", hits));

        string achievements = File.ReadAllText(Path.Combine(steamDir, "AchievementManager.cs"));
        string presence = File.ReadAllText(Path.Combine(steamDir, "RichPresenceWriter.cs"));
        // Review S1: every entry point that can publish a career status routes a
        // lineage-over state to SetBetweenCareers before it publishes, and that
        // status clears the tier key. Scanned per method body, because a
        // file-wide Contains passes as long as any one method has the guard.
        string[] guarded = { "public void SyncFromBoot(", "private void OnAvatarChanged(", "private void OnSeasonRolledOver(" };
        string[] unguarded = guarded.Where(m => !GuardsLineageOver(MethodBody(presence, m))).ToArray();
        bool betweenClearsTier = MethodBody(presence, "public void SetBetweenCareers(")
            .Contains("SetRichPresence(TierKey, string.Empty)", StringComparison.Ordinal);
        Check("13-7 source scan: AchievementManager takes its tier achievements from SteamRules; RichPresenceWriter takes tier and status tokens from it, clears the tier key for a team-less avatar and between careers, and SyncFromBoot, OnAvatarChanged and OnSeasonRolledOver each route a finished lineage to SetBetweenCareers before publishing",
            achievements.Contains("SteamRules.TierAchievements(", StringComparison.Ordinal)
            && presence.Contains("SteamRules.TierToken(", StringComparison.Ordinal)
            && presence.Contains("SteamRules.StatusToken(", StringComparison.Ordinal)
            && presence.Contains("SetRichPresence(TierKey, hasTeam ?", StringComparison.Ordinal)
            && betweenClearsTier && unguarded.Length == 0,
            unguarded.Length == 0 ? (betweenClearsTier ? "3/3 guarded" : "SetBetweenCareers keeps the tier key") : "unguarded: " + string.Join(", ", unguarded));
    }

    /// <summary>
    /// The text from a method's signature to the next member signature (or the
    /// end of the file); empty when the signature is missing, which fails
    /// the caller's check.
    /// </summary>
    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }
        int next = source.Length;
        foreach (string member in new[] { "\n    public ", "\n    private ", "\n    internal ", "\n    /// <summary>" })
        {
            int at = source.IndexOf(member, start + signature.Length, StringComparison.Ordinal);
            if (at >= 0 && at < next)
            {
                next = at;
            }
        }
        return source[start..next];
    }

    /// <summary>True when the body calls LineageIsOver and SetBetweenCareers before its first career publish.</summary>
    private static bool GuardsLineageOver(string body)
    {
        int guard = body.IndexOf("if (LineageIsOver())", StringComparison.Ordinal);
        int between = body.IndexOf("SetBetweenCareers();", StringComparison.Ordinal);
        int publish = body.IndexOf("PublishCareerPresence();", StringComparison.Ordinal);
        return guard >= 0 && between > guard && publish > between;
    }

    // ------------------------------------------------------------------
    // GetStandings vs selection and Team_Seasons
    // ------------------------------------------------------------------

    /// <summary>
    /// A synthetic regular season (the stronger team by <see cref="HashPower"/> wins every game by a
    /// seeded score; no sim, no stat rows) with the <paramref name="forfeitAt"/>-th row written as a
    /// forfeit instead (the stronger team wins regulation-innings–0, spec §4). Writes only the first
    /// <paramref name="limit"/> rows. Returns the rows written.
    /// </summary>
    private static int WriteRippleSeason(Slice134World w, LeagueTier tier, int year, ulong seed, int forfeitAt, int limit = int.MaxValue)
    {
        w.Sim(tier).PrepareSeason(year);
        var rows = new List<ScheduledGameRow>();
        w.Schedule.LoadRegularSeason(year, tier, rows);
        var rng = new RngState(seed);
        int written = 0;
        w.Db.RunInBatch(() =>
        {
            for (int i = 0; i < rows.Count && i < limit; i++)
            {
                ScheduledGameRow row = rows[i];
                bool homeWins = HashPower(row.HomeTeamId) > HashPower(row.AwayTeamId);
                if (i == forfeitAt)
                {
                    int innings = TierRules.RegulationInnings(tier);
                    w.Schedule.RecordResult(row.GameId, 2, homeWins ? innings : 0, homeWins ? 0 : innings);
                }
                else
                {
                    int winner = 2 + rng.NextInt(8);
                    int loser = rng.NextInt(winner);
                    w.Schedule.RecordFinal(row.GameId, homeWins ? winner : loser, homeWins ? loser : winner);
                }
                written++;
            }
        });
        return written;
    }

    /// <summary>
    /// True when every group's rows run best win% first, compared exactly as
    /// Standings.Order compares them (SeasonTable.CompareFraction: a 0-0 record
    /// reads as ½, so a 0-2 row ahead of a 0-0 row fails; review N1).
    /// </summary>
    private static bool GroupsOrderedByWinPct(StandingsView view) =>
        view.Groups.All(g => g.Rows.Zip(g.Rows.Skip(1)).All(p =>
            SeasonTable.CompareFraction(p.First.Wins, p.First.Wins + p.First.Losses,
                p.Second.Wins, p.Second.Wins + p.Second.Losses) >= 0));

    /// <summary>True when every view row's W/L/RS/RA equals its Team_Seasons row (and every team has one).</summary>
    private static bool ViewMatchesTeamSeasons(StandingsView view, List<TeamSeasonRow> seasons, out string detail)
    {
        int rows = 0;
        var mismatches = new List<string>();
        foreach (StandingsGroupView group in view.Groups)
        {
            foreach (StandingsRowView r in group.Rows)
            {
                rows++;
                int at = seasons.FindIndex(x => x.TeamId == r.TeamId);
                if (at < 0 || seasons[at].Wins != r.Wins || seasons[at].Losses != r.Losses
                    || seasons[at].RunsScored != r.RunsScored || seasons[at].RunsAllowed != r.RunsAllowed)
                {
                    mismatches.Add(r.TeamId.ToString());
                }
            }
        }
        detail = mismatches.Count == 0 ? $"{rows} rows" : "mismatch: " + string.Join(",", mismatches);
        return rows > 0 && rows == seasons.Count && mismatches.Count == 0;
    }

    private static void RunStandingsViewChecks(string schemaPath)
    {
        Slice134World w = World134(schemaPath);
        LeagueDirectory leagues = w.Leagues(LeagueTier.HS, LeagueTier.College, LeagueTier.Pro);
        var playoffs = new PlayoffManager(w.Db, w.Baseball, leagues);

        // ---- HS: top 3 per conference = seeds 1-3 ----
        int hsYear = Slice137BaseYear;
        int hsGames = WriteRippleSeason(w, LeagueTier.HS, hsYear, 0x1370_01UL, forfeitAt: 7);
        LeagueSimulator hs = w.Sim(LeagueTier.HS);
        int selectionDay = PostseasonCalendar.SelectionDay(LeagueTier.HS);
        DrivePostseason(playoffs, hs, hsYear, selectionDay, selectionDay);
        StandingsView hsView = playoffs.GetStandings(hsYear, LeagueTier.HS);
        var hsSeasons = new List<TeamSeasonRow>();
        w.Queries.LoadTeamSeasons(hsYear, LeagueTier.HS, hsSeasons);
        bool hsShape = hsView.Groups.Count == 2 && hsView.Groups.All(g => g.DivisionId is null && g.Rows.Count == 6)
            && hsView.Groups.Select(g => g.ConferenceId).Distinct().Count() == 2;
        bool hsSeeds = hsView.Groups.All(g =>
            g.Rows.Take(3).Select(r => r.Seed).SequenceEqual(new int?[] { 1, 2, 3 }) && g.Rows.Skip(3).All(r => !r.Seed.HasValue));
        Check("13-7 GetStandings vs selection (HS): two conference groups of six, no divisions; each conference's top three rows are seeds 1, 2, 3 in order",
            hsShape && hsSeeds && GroupsOrderedByWinPct(hsView),
            string.Join(" | ", hsView.Groups.Select(g => string.Join(",", g.Rows.Select(r => $"{r.TeamId}#{r.Seed?.ToString() ?? "-"}")))));
        bool hsMatches = ViewMatchesTeamSeasons(hsView, hsSeasons, out string hsDetail);
        long hsForfeits = CountRows(w.Db, $"SELECT COUNT(*) FROM Schedule_Games WHERE season_year = {hsYear} AND tier = 0 AND phase = 0 AND status = 2;");
        int hsViewGames = hsView.Groups.Sum(g => g.Rows.Sum(r => r.Wins + r.Losses));
        Check("13-7 GetStandings vs Team_Seasons (HS): every row's W/L/RS/RA equals its Team_Seasons row, and the one forfeit counts as a W, an L and its runs",
            hsMatches && hsForfeits == 1 && hsViewGames == 2 * hsGames, $"{hsDetail}, {hsForfeits} forfeit, {hsViewGames / 2} games");

        // ---- Pro: the four division leaders of each conference = seeds 1-4 ----
        int proYear = Slice137BaseYear + 10;
        int proGames = WriteRippleSeason(w, LeagueTier.Pro, proYear, 0x1370_02UL, forfeitAt: 11);
        LeagueSimulator pro = w.Sim(LeagueTier.Pro);
        int proSelection = PostseasonCalendar.SelectionDay(LeagueTier.Pro);
        DrivePostseason(playoffs, pro, proYear, proSelection, proSelection);
        StandingsView proView = playoffs.GetStandings(proYear, LeagueTier.Pro);
        var proSeasons = new List<TeamSeasonRow>();
        w.Queries.LoadTeamSeasons(proYear, LeagueTier.Pro, proSeasons);
        bool proShape = proView.Groups.Count == 8 && proView.Groups.All(g => g.DivisionId.HasValue && g.Rows.Count == 4)
            && proView.Groups.Select(g => g.ConferenceId).Distinct().Count() == 2;
        bool leadersAreTopFour = proView.Groups.GroupBy(g => g.ConferenceId).All(conference =>
        {
            var leaders = conference.Select(g => g.Rows[0].TeamId).OrderBy(x => x).ToList();
            var topFour = proView.Groups.Where(g => g.ConferenceId == conference.Key)
                .SelectMany(g => g.Rows).Where(r => r.Seed is >= 1 and <= 4).Select(r => r.TeamId).OrderBy(x => x).ToList();
            return leaders.Count == 4 && leaders.SequenceEqual(topFour);
        });
        bool wildCards = proView.Groups.GroupBy(g => g.ConferenceId).All(conference =>
            conference.SelectMany(g => g.Rows).Count(r => r.Seed is 5 or 6) == 2
            && conference.SelectMany(g => g.Rows).Where(r => r.Seed is 5 or 6).All(r => conference.All(g => g.Rows[0].TeamId != r.TeamId)));
        Check("13-7 GetStandings vs selection (Pro): two conferences of four divisions of four; each conference's division leaders are exactly its seed 1-4 teams, and seeds 5-6 are non-leaders",
            proShape && leadersAreTopFour && wildCards && GroupsOrderedByWinPct(proView));
        bool proMatches = ViewMatchesTeamSeasons(proView, proSeasons, out string proDetail);
        long proForfeits = CountRows(w.Db, $"SELECT COUNT(*) FROM Schedule_Games WHERE season_year = {proYear} AND tier = 2 AND phase = 0 AND status = 2;");
        int proViewGames = proView.Groups.Sum(g => g.Rows.Sum(r => r.Wins + r.Losses));
        Check("13-7 GetStandings vs Team_Seasons (Pro): every row's W/L/RS/RA equals its Team_Seasons row, forfeit included",
            proMatches && proForfeits == 1 && proViewGames == 2 * proGames, $"{proDetail}, {proForfeits} forfeit, {proViewGames / 2} games");

        // ---- College: three divisions per conference, and every row carries the written national rank ----
        int collegeYear = Slice137BaseYear + 20;
        int collegeGames = WriteRippleSeason(w, LeagueTier.College, collegeYear, 0x1370_03UL, forfeitAt: 5);
        LeagueSimulator college = w.Sim(LeagueTier.College);
        int collegeSelection = PostseasonCalendar.SelectionDay(LeagueTier.College);
        DrivePostseason(playoffs, college, collegeYear, collegeSelection, collegeSelection);
        StandingsView collegeView = playoffs.GetStandings(collegeYear, LeagueTier.College);
        var collegeSeasons = new List<TeamSeasonRow>();
        w.Queries.LoadTeamSeasons(collegeYear, LeagueTier.College, collegeSeasons);
        bool collegeShape = collegeView.Groups.Count == 6 && collegeView.Groups.All(g => g.DivisionId.HasValue && g.Rows.Count == 4)
            && collegeView.Groups.Select(g => g.ConferenceId).Distinct().Count() == 2;
        var viewRows = collegeView.Groups.SelectMany(g => g.Rows).ToList();
        bool ranksWritten = viewRows.Count == 24
            && viewRows.Select(r => r.NationalRank ?? 0).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 24))
            && viewRows.All(r => collegeSeasons.Any(ts => ts.TeamId == r.TeamId && ts.NationalRank == r.NationalRank && ts.Seed == r.Seed));
        bool collegeSeeds = viewRows.Count(r => r.Seed.HasValue) == 17
            && collegeView.Groups.GroupBy(g => g.ConferenceId).All(c =>
                c.SelectMany(g => g.Rows).Where(r => r.Seed.HasValue).Select(r => r.Seed!.Value).OrderBy(x => x)
                    .SequenceEqual(Enumerable.Range(1, c.SelectMany(g => g.Rows).Count(r => r.Seed.HasValue))));
        Check("13-7 GetStandings vs selection (College): two conferences of three divisions of four; every row carries its written national rank (1-24 once each) and the 17 auto-bids their 1..n conference seeds",
            collegeShape && ranksWritten && collegeSeeds && GroupsOrderedByWinPct(collegeView));
        bool collegeMatches = ViewMatchesTeamSeasons(collegeView, collegeSeasons, out string collegeDetail);
        long collegeForfeits = CountRows(w.Db, $"SELECT COUNT(*) FROM Schedule_Games WHERE season_year = {collegeYear} AND tier = 1 AND phase = 0 AND status = 2;");
        int collegeViewGames = viewRows.Sum(r => r.Wins + r.Losses);
        Check("13-7 GetStandings vs Team_Seasons (College): every row's W/L/RS/RA equals its Team_Seasons row, forfeit included",
            collegeMatches && collegeForfeits == 1 && collegeViewGames == 2 * collegeGames,
            $"{collegeDetail}, {collegeForfeits} forfeit, {collegeViewGames / 2} games");

        // ---- Mid-season, before selection: a read of the completed games only ----
        int midYear = Slice137BaseYear + 30;
        int midWritten = WriteRippleSeason(w, LeagueTier.HS, midYear, 0x1370_30UL, forfeitAt: -1, limit: 20);
        long teamSeasonsBefore = CountRows(w.Db, $"SELECT COUNT(*) FROM Team_Seasons WHERE season_year = {midYear};");
        StandingsView mid = playoffs.GetStandings(midYear, LeagueTier.HS);
        long teamSeasonsAfter = CountRows(w.Db, $"SELECT COUNT(*) FROM Team_Seasons WHERE season_year = {midYear};");
        int midGamesInView = mid.Groups.Sum(g => g.Rows.Sum(r => r.Wins + r.Losses));
        Check("13-7 GetStandings, mid-season before selection: records come from the completed games only, no rank or seed yet, groups run best win% first, and the view writes nothing",
            midWritten == 20 && midGamesInView == 2 * midWritten
            && mid.Groups.All(g => g.Rows.All(r => !r.NationalRank.HasValue && !r.Seed.HasValue))
            && GroupsOrderedByWinPct(mid) && teamSeasonsBefore == 0 && teamSeasonsAfter == 0,
            $"{midWritten} games, {midGamesInView} W+L in the view, Team_Seasons {teamSeasonsBefore}->{teamSeasonsAfter}");
    }
}
