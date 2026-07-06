using DirtAndDiamonds.Core;
using DirtAndDiamonds.Narrative.Events;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (a permanent sibling of the swappable screen under
/// Main, per Main.tscn) that renders the avatar's paused Gritty Event choice.
/// Self-driven: polls <see cref="EventConsequenceApplier.HasPendingChoice"/>
/// every frame and only rebuilds its choice buttons on a change of which fire
/// is pending (dirty-flag pattern, mirroring BaseballDashboard's
/// _awaitingPendingGame check) — never every frame while the same choice
/// sits unanswered. UI never touches the database directly: a button press
/// only calls GameManager.Instance.GrittyEventChoices.ResolveChoice, which
/// owns every consequence write. Node paths verified against
/// EventChoiceScreen.tscn before this script was written.
/// </summary>
public sealed partial class EventChoiceScreen : Control
{
    private Label _promptLabel = null!;
    private VBoxContainer _choicesContainer = null!;

    // Identifies exactly which fire is currently rendered, so a new fire (or
    // a forfeit-and-replace while this screen wasn't looking) is detected
    // without rebuilding the buttons every idle frame.
    private string? _shownFireIdentity;

    public override void _Ready()
    {
        _promptLabel = GetNode<Label>("Screen/Layout/PromptLabel");
        _choicesContainer = GetNode<VBoxContainer>("Screen/Layout/ChoicesContainer");
    }

    public override void _Process(double delta)
    {
        EventConsequenceApplier choices = GameManager.Instance!.GrittyEventChoices;
        if (!choices.TryGetPendingChoice(out PendingGrittyChoice pending))
        {
            Visible = false;
            _shownFireIdentity = null;
            return;
        }

        Visible = true;
        string identity = $"{pending.Fired.EventId}|{pending.Fired.SubjectPlayerId}|{pending.Fired.Day}";
        if (identity == _shownFireIdentity)
        {
            return;
        }
        _shownFireIdentity = identity;
        Render(pending);
    }

    private void Render(in PendingGrittyChoice pending)
    {
        _promptLabel.Text = pending.Definition.Prompt;

        foreach (Node child in _choicesContainer.GetChildren())
        {
            child.QueueFree();
        }

        EventChoice[] eventChoices = pending.Definition.Choices;
        for (int i = 0; i < eventChoices.Length; i++)
        {
            int choiceIndex = i; // captured by value, not by the loop variable
            var button = new Button { Text = eventChoices[i].Label };
            button.Pressed += () => OnChoicePressed(choiceIndex);
            _choicesContainer.AddChild(button);
        }
    }

    private void OnChoicePressed(int choiceIndex) =>
        GameManager.Instance!.GrittyEventChoices.ResolveChoice(choiceIndex);
}
