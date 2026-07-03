using DirtAndDiamonds.Data;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Read-only snapshot of the live at-bat the driver hands to the view — the
/// UI renders this and nothing else (ui_conventions: UI is read-only over
/// simulation state; the driver owns every write). CueType/ZoneHint are the
/// v4 pre-pitch look the zone-read minigame is played against.
/// </summary>
public readonly struct AtBatViewState
{
    public readonly int AwayScore;
    public readonly int HomeScore;
    public readonly int Inning;
    public readonly bool IsTopHalf;
    public readonly int Balls;
    public readonly int Strikes;
    public readonly int Outs;
    /// <summary>3-bit base map, matching the sim (1 = 1B, 2 = 2B, 4 = 3B).</summary>
    public readonly int Bases;
    /// <summary>The (possibly blurred) pitch-type cue for the incoming pitch.</summary>
    public readonly PitchType CueType;
    /// <summary>Scouting P(in zone) implied by the cue, [0, 1].</summary>
    public readonly double ZoneHint;

    public AtBatViewState(
        int awayScore, int homeScore, int inning, bool isTopHalf,
        int balls, int strikes, int outs, int bases, PitchType cueType, double zoneHint)
    {
        AwayScore = awayScore;
        HomeScore = homeScore;
        Inning = inning;
        IsTopHalf = isTopHalf;
        Balls = balls;
        Strikes = strikes;
        Outs = outs;
        Bases = bases;
        CueType = cueType;
        ZoneHint = zoneHint;
    }
}

/// <summary>
/// The Phase 4 thin vertical slice (micro doc §12): renders the at-bat state
/// DTO and emits the player's intent — swing timing / take — upward as
/// signals. It never touches the database, never mutates sim state, and does
/// no per-frame work: labels update only inside <see cref="Render"/>, which
/// the driver calls when state actually changes (dirty-flag by construction).
/// Node paths below were verified against AtBatView.tscn via
/// godot_scene_mapper before being written.
/// </summary>
public sealed partial class AtBatView : PanelContainer
{
    /// <summary>Player committed a swing: the slider's τ ∈ [-1, +1] (§6) plus the zone read.</summary>
    [Signal]
    public delegate void SwingCommittedEventHandler(double timingError, bool guessInZone);

    /// <summary>Player took the pitch (with their zone read).</summary>
    [Signal]
    public delegate void TakeCommittedEventHandler(bool guessInZone);

    /// <summary>Cap on retained play-by-play lines before the oldest are dropped.</summary>
    [Export]
    public int MaxLogLines { get; set; } = 200;

    /// <summary>Player-facing names for the PitchType values, in enum order.</summary>
    [Export]
    public string PitchTypeNamesCsv { get; set; } = "Fastball,Breaking ball,Offspeed";

    /// <summary>Pre-pitch look line: {0} = cued pitch type, {1} = zone hint [0,1].</summary>
    [Export]
    public string PitchCueFormat { get; set; } = "Looks like: {0} — zone odds {1:P0}";

    private Label _scoreLabel = null!;
    private Label _inningLabel = null!;
    private Label _countLabel = null!;
    private Label _outsLabel = null!;
    private Label _basesLabel = null!;
    private Label _pitchCueLabel = null!;
    private RichTextLabel _playLog = null!;
    private CheckButton _zoneReadToggle = null!;
    private HSlider _timingSlider = null!;
    private Button _swingButton = null!;
    private Button _takeButton = null!;

    private string[] _pitchTypeNames = System.Array.Empty<string>();
    private int _logLines;

    public override void _Ready()
    {
        _scoreLabel = GetNode<Label>("Layout/ScoreLabel");
        _inningLabel = GetNode<Label>("Layout/InningLabel");
        _countLabel = GetNode<Label>("Layout/CountLabel");
        _outsLabel = GetNode<Label>("Layout/OutsLabel");
        _basesLabel = GetNode<Label>("Layout/BasesLabel");
        _pitchCueLabel = GetNode<Label>("Layout/PitchCueLabel");
        _playLog = GetNode<RichTextLabel>("Layout/PlayLog");
        _zoneReadToggle = GetNode<CheckButton>("Layout/Controls/ZoneReadToggle");
        _timingSlider = GetNode<HSlider>("Layout/Controls/TimingSlider");
        _swingButton = GetNode<Button>("Layout/Controls/SwingButton");
        _takeButton = GetNode<Button>("Layout/Controls/TakeButton");

        _swingButton.Pressed += OnSwingPressed;
        _takeButton.Pressed += OnTakePressed;
        _pitchTypeNames = PitchTypeNamesCsv.Split(',');
        _playLog.Clear();
    }

    public override void _ExitTree()
    {
        _swingButton.Pressed -= OnSwingPressed;
        _takeButton.Pressed -= OnTakePressed;
    }

    /// <summary>Renders one state snapshot. Called by the driver on change, never per frame.</summary>
    public void Render(in AtBatViewState state)
    {
        _scoreLabel.Text = $"{state.AwayScore} : {state.HomeScore}";
        _inningLabel.Text = $"{(state.IsTopHalf ? "▲" : "▼")} {state.Inning}";
        _countLabel.Text = $"{state.Balls}-{state.Strikes}";
        _outsLabel.Text = state.Outs.ToString();
        _basesLabel.Text = BasesGlyph(state.Bases);
        _pitchCueLabel.Text = string.Format(PitchCueFormat, PitchTypeName(state.CueType), state.ZoneHint);
        SetIntentEnabled(true);
    }

    /// <summary>Appends one play-by-play line (already-localized text from the driver's feed).</summary>
    public void AppendPlayLine(string line)
    {
        if (_logLines >= MaxLogLines)
        {
            _playLog.Clear();
            _logLines = 0;
        }
        _playLog.AppendText(line);
        _playLog.AppendText("\n");
        _logLines++;
    }

    private void OnSwingPressed()
    {
        SetIntentEnabled(false);
        EmitSignal(SignalName.SwingCommitted, _timingSlider.Value, _zoneReadToggle.ButtonPressed);
    }

    private void OnTakePressed()
    {
        SetIntentEnabled(false);
        EmitSignal(SignalName.TakeCommitted, _zoneReadToggle.ButtonPressed);
    }

    /// <summary>Intent controls lock while the sim resolves the pitch off the UI thread.</summary>
    private void SetIntentEnabled(bool enabled)
    {
        _swingButton.Disabled = !enabled;
        _takeButton.Disabled = !enabled;
        _timingSlider.Editable = enabled;
        _zoneReadToggle.Disabled = !enabled;
    }

    private string PitchTypeName(PitchType type)
    {
        int index = (int)type;
        return index >= 0 && index < _pitchTypeNames.Length ? _pitchTypeNames[index] : type.ToString();
    }

    /// <summary>Diamond glyphs for the 3-bit base map: 1B-2B-3B, filled when occupied.</summary>
    private static string BasesGlyph(int bases)
    {
        Span<char> glyphs = stackalloc char[5];
        glyphs[0] = (bases & 0b001) != 0 ? '◆' : '◇';
        glyphs[1] = ' ';
        glyphs[2] = (bases & 0b010) != 0 ? '◆' : '◇';
        glyphs[3] = ' ';
        glyphs[4] = (bases & 0b100) != 0 ? '◆' : '◇';
        return new string(glyphs);
    }
}
