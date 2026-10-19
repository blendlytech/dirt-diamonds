using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// The schema v14 clean-break dialog (league_restructure.md §14, 13-1 brief
/// §3.2.5): shown by Main in place of every other screen while
/// <see cref="GameManager.PendingSaveRefusal"/> is set. It reads that flag and
/// emits the player's intent, nothing more. It never touches the save file,
/// the database or any <c>RngState</c>.
///
/// An older save offers <b>Start a new career</b> (→
/// <see cref="GameManager.ConfirmCleanBreak"/>, which moves the save aside as
/// <c>dirt_and_diamonds.v13.bak</c> and boots a new world) or <b>Quit</b>. A
/// newer build's save offers only Quit: it is never renamed.
///
/// All player-facing copy lives in CleanBreakDialog.tscn (Godot's
/// translation-ready scene text), never in this script. Uses the current
/// theme as is; slice 15b restyles it. Node paths verified against
/// CleanBreakDialog.tscn.
/// </summary>
public sealed partial class CleanBreakDialog : Control
{
    /// <summary>Raised after the new career booted, so Main can route to the next screen.</summary>
    [Signal]
    public delegate void CleanBreakConfirmedEventHandler();

    private Label _olderBodyLabel = null!;
    private Label _olderSubtitleLabel = null!;
    private Label _newerBodyLabel = null!;
    private Button _startButton = null!;
    private Button _quitButton = null!;

    public override void _Ready()
    {
        _olderBodyLabel = GetNode<Label>("Center/Panel/Layout/OlderBodyLabel");
        _olderSubtitleLabel = GetNode<Label>("Center/Panel/Layout/OlderSubtitleLabel");
        _newerBodyLabel = GetNode<Label>("Center/Panel/Layout/NewerBodyLabel");
        _startButton = GetNode<Button>("Center/Panel/Layout/Buttons/StartButton");
        _quitButton = GetNode<Button>("Center/Panel/Layout/Buttons/QuitButton");

        bool olderSave = GameManager.Instance!.PendingSaveRefusal == SaveRefusal.OlderSave;
        _olderBodyLabel.Visible = olderSave;
        _olderSubtitleLabel.Visible = olderSave;
        _newerBodyLabel.Visible = !olderSave;
        _startButton.Visible = olderSave;

        _startButton.Pressed += OnStartPressed;
        _quitButton.Pressed += OnQuitPressed;
        (olderSave ? _startButton : _quitButton).GrabFocus();
    }

    public override void _ExitTree()
    {
        _startButton.Pressed -= OnStartPressed;
        _quitButton.Pressed -= OnQuitPressed;
    }

    private void OnStartPressed()
    {
        // One answer per dialog: a second press can't re-enter the boot.
        _startButton.Disabled = true;
        GameManager.Instance!.ConfirmCleanBreak();
        EmitSignal(SignalName.CleanBreakConfirmed);
    }

    private void OnQuitPressed() => GetTree().Quit();
}
