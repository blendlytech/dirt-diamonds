using DirtAndDiamonds.Core;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (a permanent sibling of the swappable screen under
/// Main, per Main.tscn — declared last so it draws above the succession
/// overlay) that announces a newborn heir off
/// <see cref="CareerManager.TryDequeuePendingBirth"/>. Unlike SuccessionScreen
/// (and the Burner Phone's pending-choice thread) this is deliberately NOT modal: there is no decision
/// to forfeit here, so it neither joins BaseballDashboard's day-advance gate
/// nor blocks input to anything beneath it — the root and every non-button
/// node are corner-anchored and mouse_filter = Ignore in the .tscn (a
/// full-rect click-blocker here would silently eat clicks on AtBatView's
/// Swing/Take buttons, which the day-advance gate never covers). Self-driven,
/// simpler than the other two overlays' dirty-flag identity check since a
/// dequeued announcement is owned by this screen until dismissed — no
/// re-render-on-change logic needed. Node paths verified against
/// BirthNotificationScreen.tscn before this script was written.
/// </summary>
public sealed partial class BirthNotificationScreen : Control
{
    [Export]
    public string AnnouncementFormat { get; set; } = "A child is born! {0} {1} joins the family.";

    [Export]
    public string WithPartnerFormat { get; set; } = "You and {0} {1} are parents now.";

    private Label _messageLabel = null!;
    private Label _partnerLabel = null!;
    private Button _dismissButton = null!;

    private bool _showing;

    public override void _Ready()
    {
        _messageLabel = GetNode<Label>("Panel/Layout/MessageLabel");
        _partnerLabel = GetNode<Label>("Panel/Layout/PartnerLabel");
        _dismissButton = GetNode<Button>("Panel/Layout/DismissButton");

        _dismissButton.Pressed += OnDismissPressed;
    }

    public override void _ExitTree()
    {
        _dismissButton.Pressed -= OnDismissPressed;
    }

    public override void _Process(double delta)
    {
        if (_showing)
        {
            return;
        }
        if (GameManager.Instance!.Career.TryDequeuePendingBirth(out BirthAnnouncement announcement))
        {
            Render(in announcement);
        }
    }

    private void Render(in BirthAnnouncement announcement)
    {
        _messageLabel.Text = string.Format(AnnouncementFormat, announcement.ChildFirstName, announcement.ChildLastName);

        bool hasPartner = announcement.PartnerFirstName is not null;
        _partnerLabel.Visible = hasPartner;
        if (hasPartner)
        {
            _partnerLabel.Text = string.Format(WithPartnerFormat, announcement.PartnerFirstName, announcement.PartnerLastName);
        }

        _showing = true;
        Visible = true;
    }

    private void OnDismissPressed()
    {
        _showing = false;
        Visible = false;
    }
}
