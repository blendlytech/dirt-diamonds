using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// The ad-hoc state colors the UI shares until the theming slice
/// (GAME_IMPROVEMENTS Slice E) lands a real palette — one place to retint.
/// Danger matches the red Main.cs's failed-boot banner already ships.
/// </summary>
public static class UiColors
{
    public static readonly Color Danger = new(1f, 0.3f, 0.3f);
    public static readonly Color Warning = new(1f, 0.78f, 0.35f);
}
