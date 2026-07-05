using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Economy.Equipment;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (a permanent sibling of the swappable screen under
/// Main, per Main.tscn) that is Phase 8e's Layer 3: the gear shop
/// (docs/design/equipment_quality.md §7). Follows ScheduleScreen's
/// corner-anchored, non-blocking idiom — bottom-LEFT here, since the schedule
/// panel owns bottom-right — and its visibility rule (shown whenever an
/// avatar exists; buying gear is always optional, so this never joins any
/// day-advance gate).
///
/// Render path is fully in-memory per ui_conventions' read-only rule: funds
/// from the Life sim's mirror (<c>LifeSim.TryGetFunds</c>), owned tier from
/// the <c>GameManager.Gear</c> ledger — the database is only ever touched by
/// <see cref="EquipmentService.TryPurchase"/> on an actual click, which
/// re-validates everything against the authoritative Players row regardless
/// of what the buttons showed (a stale click can never corrupt state). Labels
/// are dirty-flag gated on (funds, owned quality); button Disabled states
/// mirror TryPurchase's own predicate. Node paths verified against
/// EquipmentShopScreen.tscn (godot_scene_mapper) before this script was
/// written.
/// </summary>
public sealed partial class EquipmentShopScreen : Control
{
    [Export]
    public string FundsFormat { get; set; } = "Funds: ${0:F0}";

    [Export]
    public string OwnedFormat { get; set; } = "Gear: {0}";

    [Export]
    public string BuyFormat { get; set; } = "Buy ${0:F0}";

    [Export]
    public string PurchasedFormat { get; set; } = "Bought {0}.";

    [Export]
    public string CannotAffordText { get; set; } = "Not enough cash for that.";

    [Export]
    public string NotAnUpgradeText { get; set; } = "You already own gear that good.";

    /// <summary>Tier display names indexed by quality 0–3 (player-facing copy, editor-overridable).</summary>
    [Export]
    public string[] TierNames { get; set; } =
    {
        "Standard issue", "Quality gear", "Premium gear", "Custom pro gear",
    };

    private Label _fundsLabel = null!;
    private Label _ownedLabel = null!;
    private Button _qualityBuyButton = null!;
    private Button _premiumBuyButton = null!;
    private Button _proBuyButton = null!;
    private Label _statusLabel = null!;

    // Dirty-flag identity for the polled labels/buttons (ui_conventions.md:
    // no per-frame string formatting) — reformatted only when the mirror
    // funds or the ledger's owned tier actually moved.
    private double _shownFunds = double.NaN;
    private int _shownOwnedQuality = -1;

    public override void _Ready()
    {
        _fundsLabel = GetNode<Label>("Panel/Layout/FundsLabel");
        _ownedLabel = GetNode<Label>("Panel/Layout/OwnedLabel");
        _qualityBuyButton = GetNode<Button>("Panel/Layout/QualityRow/QualityBuyButton");
        _premiumBuyButton = GetNode<Button>("Panel/Layout/PremiumRow/PremiumBuyButton");
        _proBuyButton = GetNode<Button>("Panel/Layout/ProRow/ProBuyButton");
        _statusLabel = GetNode<Label>("Panel/Layout/StatusLabel");

        // Sticker prices come from the service's one price table — the scene
        // never hardcodes a dollar figure, so a retune is a single data edit.
        _qualityBuyButton.Text = string.Format(BuyFormat, EquipmentService.PriceForQuality(1));
        _premiumBuyButton.Text = string.Format(BuyFormat, EquipmentService.PriceForQuality(2));
        _proBuyButton.Text = string.Format(BuyFormat, EquipmentService.PriceForQuality(3));

        _qualityBuyButton.Pressed += OnBuyQualityPressed;
        _premiumBuyButton.Pressed += OnBuyPremiumPressed;
        _proBuyButton.Pressed += OnBuyProPressed;
    }

    public override void _ExitTree()
    {
        _qualityBuyButton.Pressed -= OnBuyQualityPressed;
        _premiumBuyButton.Pressed -= OnBuyPremiumPressed;
        _proBuyButton.Pressed -= OnBuyProPressed;
    }

    public override void _Process(double delta)
    {
        GameManager game = GameManager.Instance!;
        if (!game.Career.HasAvatar)
        {
            Visible = false;
            return;
        }
        Visible = true;

        string avatarId = game.Career.AvatarPlayerId;
        int owned = game.Gear.QualityFor(avatarId);
        if (!game.LifeSim.TryGetFunds(avatarId, out double funds))
        {
            funds = 0.0;
        }

        if (funds != _shownFunds || owned != _shownOwnedQuality)
        {
            _shownFunds = funds;
            _shownOwnedQuality = owned;
            _fundsLabel.Text = string.Format(FundsFormat, funds);
            _ownedLabel.Text = string.Format(OwnedFormat, TierNames[owned]);
            RefreshBuyButton(_qualityBuyButton, 1, owned, funds);
            RefreshBuyButton(_premiumBuyButton, 2, owned, funds);
            RefreshBuyButton(_proBuyButton, 3, owned, funds);
        }
    }

    private static void RefreshBuyButton(Button button, int quality, int owned, double funds) =>
        button.Disabled = quality <= owned || funds < EquipmentService.PriceForQuality(quality);

    private void OnBuyQualityPressed() => Purchase(1);

    private void OnBuyPremiumPressed() => Purchase(2);

    private void OnBuyProPressed() => Purchase(3);

    private void Purchase(int quality)
    {
        GameManager game = GameManager.Instance!;
        if (!game.Career.HasAvatar)
        {
            return;
        }
        try
        {
            bool bought = game.GearShop.TryPurchase(
                game.Career.AvatarPlayerId, quality, game.State.CurrentDay,
                out EquipmentPurchaseFailure failure);
            _statusLabel.Text = bought
                ? string.Format(PurchasedFormat, TierNames[quality])
                : failure switch
                {
                    EquipmentPurchaseFailure.InsufficientFunds => CannotAffordText,
                    EquipmentPurchaseFailure.NotAnUpgrade => NotAnUpgradeText,
                    _ => failure.ToString(),
                };
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
        }
        _statusLabel.Visible = true;
    }
}
