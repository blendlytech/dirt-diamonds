using DirtAndDiamonds.Core;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Root of Main.tscn (the project's main scene). Routes boot into either the
/// new-game avatar creation screen or straight to the attended-game screen,
/// depending on whether a career already exists. GameManager is an autoload
/// so it boots (and restores any saved avatar) before this scene's _Ready
/// runs, making Career.HasAvatar reliable here. Swapping the child on
/// AvatarCreated is the only scene-tree work this class does.
/// </summary>
public sealed partial class Main : Node
{
    [Export]
    public PackedScene NewGameScreenScene { get; set; } = null!;

    [Export]
    public PackedScene AttendedGameScreenScene { get; set; } = null!;

    public override void _Ready()
    {
        ShowAppropriateScreen();
    }

    private void ShowAppropriateScreen()
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        if (GameManager.Instance!.Career.HasAvatar)
        {
            AddChild(AttendedGameScreenScene.Instantiate());
        }
        else
        {
            var newGameScreen = (NewGameScreen)NewGameScreenScene.Instantiate();
            newGameScreen.AvatarCreated += OnAvatarCreated;
            AddChild(newGameScreen);
        }
    }

    private void OnAvatarCreated() => ShowAppropriateScreen();
}
