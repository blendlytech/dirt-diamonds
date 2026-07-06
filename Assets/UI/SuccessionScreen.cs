using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Core;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (a permanent sibling of the swappable screen under
/// Main, per Main.tscn) covering the two heir_mechanics.md §5/§6 UI beats:
/// the heir reveal/choice (<see cref="CareerManager.HasPendingSuccessionChoice"/>)
/// and a persistent game-over banner (<see cref="CareerManager.IsLineageOver"/>).
/// Self-driven, dirty-flag polling in _Process (mirrors BaseballDashboard's
/// _awaitingPendingGame / BurnerPhone's pending-fire identity pattern) — the
/// candidate buttons rebuild only when the pending candidate set actually
/// changes, never every idle frame. UI never touches the database directly: a
/// button press only calls CareerManager.ResolvePendingSuccession, which owns
/// every consequence write. Node paths verified against SuccessionScreen.tscn
/// before this script was written.
/// </summary>
public sealed partial class SuccessionScreen : Control
{
    [Export]
    public string CandidateFormat { get; set; } = "{0} {1} (age {2}) — interest {3} — {4}";

    [Export]
    public string BatterRatingsFormat { get; set; } = "Power {0} / Contact {1} / Discipline {2}";

    [Export]
    public string PitcherRatingsFormat { get; set; } = "Stuff {0} / Control {1} / Stamina {2}";

    [Export]
    public string NoHeirsText { get; set; } = "Your bloodline ends here — no children to carry it on.";

    [Export]
    public string NoWillingHeirText { get; set; } = "Your bloodline ends here — no child was willing to play.";

    [Export]
    public string NoPlayableHeirText { get; set; } = "Your bloodline ends here — no child was old enough to take over.";

    private Control _choicePanel = null!;
    private VBoxContainer _candidatesContainer = null!;
    private Control _gameOverPanel = null!;
    private Label _gameOverLabel = null!;

    // Dirty-flag identity for the candidate list: count + first heir id is
    // enough to detect "a new pending choice replaced the old one" without a
    // per-frame allocation (the candidate set only ever changes once per
    // season at most, so collisions have no realistic way to occur).
    private int _shownCandidateCount = -1;
    private string? _shownFirstHeirId;
    private bool _shownGameOver;

    public override void _Ready()
    {
        _choicePanel = GetNode<Control>("ChoicePanel");
        _candidatesContainer = GetNode<VBoxContainer>("ChoicePanel/Layout/CandidatesContainer");
        _gameOverPanel = GetNode<Control>("GameOverPanel");
        _gameOverLabel = GetNode<Label>("GameOverPanel/GameOverLabel");
    }

    public override void _Process(double delta)
    {
        CareerManager career = GameManager.Instance!.Career;

        bool showChoice = career.TryGetPendingSuccessionChoice(out IReadOnlyList<HeirCandidate> candidates);
        _choicePanel.Visible = showChoice;
        if (showChoice)
        {
            bool identical = candidates!.Count == _shownCandidateCount
                && (candidates.Count == 0 || candidates[0].HeirId == _shownFirstHeirId);
            if (!identical)
            {
                _shownCandidateCount = candidates.Count;
                _shownFirstHeirId = candidates.Count > 0 ? candidates[0].HeirId : null;
                RenderCandidates(candidates);
            }
        }
        else
        {
            _shownCandidateCount = -1;
            _shownFirstHeirId = null;
        }

        bool showGameOver = career.IsLineageOver;
        _gameOverPanel.Visible = showGameOver;
        if (showGameOver && !_shownGameOver)
        {
            _gameOverLabel.Text = TextForReason(career.LineageOverReason);
        }
        _shownGameOver = showGameOver;

        Visible = showChoice || showGameOver;
    }

    private void RenderCandidates(IReadOnlyList<HeirCandidate> candidates)
    {
        foreach (Node child in _candidatesContainer.GetChildren())
        {
            child.QueueFree();
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            HeirCandidate candidate = candidates[i];
            string heirId = candidate.HeirId; // captured by value, not by the loop variable
            var button = new Button { Text = FormatCandidate(in candidate) };
            button.Pressed += () => GameManager.Instance!.Career.ResolvePendingSuccession(heirId);
            _candidatesContainer.AddChild(button);
        }
    }

    private string FormatCandidate(in HeirCandidate candidate)
    {
        string ratingsText = candidate.Ratings.IsPitcher
            ? string.Format(PitcherRatingsFormat, candidate.Ratings.PitStuff, candidate.Ratings.PitControl, candidate.Ratings.PitStamina)
            : string.Format(BatterRatingsFormat, candidate.Ratings.BatPower, candidate.Ratings.BatContact, candidate.Ratings.BatDiscipline);
        return string.Format(CandidateFormat, candidate.FirstName, candidate.LastName, candidate.Age, candidate.BaseballInterest, ratingsText);
    }

    private string TextForReason(LineageFailure reason) => reason switch
    {
        LineageFailure.NoHeirs => NoHeirsText,
        LineageFailure.NoWillingHeir => NoWillingHeirText,
        LineageFailure.NoPlayableHeir => NoPlayableHeirText,
        _ => string.Empty,
    };
}
