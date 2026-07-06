using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Equipment;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Narrative.Contacts;
using DirtAndDiamonds.Narrative.Events;
using DirtAndDiamonds.Simulation.Life;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Phase 10b: the right panel of the two-panel shell. The Messages tab is the
/// Burner Phone's threaded read-model over the narrative-log write
/// (presentation_layer_narrative.md §4) — a contact list (most-recent-first,
/// unread marker) and the selected contact's thread. When a gritty-event
/// choice is pending for the avatar, the phone auto-opens that contact's
/// thread, renders the prompt as one more (unanswered) incoming bubble, and
/// renders the choices as reply-chip buttons — <see cref="EventChoiceScreen"/>'s
/// exact <c>TryGetPendingChoice</c>/<c>ResolveChoice</c> seam and dirty-flag
/// identity check, reskinned into the thread rather than a separate modal
/// (EventChoiceScreen retires in this same phase). UI never touches the
/// database directly: history comes from <see cref="GameManager.NarrativeLog"/>'s
/// read-back, reloaded only on a pending-fire transition (never per-frame),
/// matching BaseballDashboard's dirty-flag discipline. Node paths verified
/// against BurnerPhone.tscn (authored alongside this script) via
/// godot_scene_mapper before wiring.
/// </summary>
public sealed partial class BurnerPhone : PanelContainer
{
    /// <summary>
    /// 10d: emitted when a Bank-tab hustle button is pressed. Carries a
    /// <see cref="WorkActivity"/> cast to int (Godot signals need Variant-
    /// friendly params). No bypass of the day-plan gate — the listener just
    /// pre-selects ScheduleScreen's "Work as" dropdown; the player still has
    /// to confirm the plan and advance the day, per ui_conventions' rule that
    /// UI never mutates simulation state directly. Main.cs is the one
    /// listener, wiring this sibling-to-sibling seam without either screen
    /// reaching into the other's tree.
    /// </summary>
    [Signal]
    public delegate void HustleLaunchRequestedEventHandler(int workActivity);

    [Export]
    public string TimestampFormat { get; set; } = "Season {0}, day {1}";

    [Export]
    public string UnreadMarker { get; set; } = "● ";

    [Export]
    public string NoContactSelectedText { get; set; } = "Select a contact";

    [Export]
    public string FundsFormat { get; set; } = "${0:N0}";

    [Export]
    public string CostOfLivingFormat { get; set; } = "Cost of living: ${0:F0}/wk — next bill in {1}d";

    /// <summary>Tier display names indexed by quality 0–3, mirroring EquipmentShopScreen's own copy.</summary>
    [Export]
    public string[] TierNames { get; set; } =
    {
        "Standard issue", "Quality gear", "Premium gear", "Custom pro gear",
    };

    [Export]
    public string EquipmentTierFormat { get; set; } = "Equipped: {0}";

    [Export]
    public string EquipmentNextFormat { get; set; } = "Next: {0} — ${1:F0}";

    [Export]
    public string TopTierOwnedText { get; set; } = "Top-tier gear owned.";

    private ItemList _contactList = null!;
    private Label _threadHeaderLabel = null!;
    private VBoxContainer _threadContainer = null!;
    private VBoxContainer _choicesContainer = null!;

    private Label _fundsValueLabel = null!;
    private Label _costOfLivingLabel = null!;
    private Label _equipmentTierLabel = null!;
    private Label _equipmentNextLabel = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _sleepBar = null!;
    private ProgressBar _hygieneBar = null!;
    private ProgressBar _socialBar = null!;
    private ProgressBar _fitnessBar = null!;
    private Button _narcoticsButton = null!;
    private Button _fencingButton = null!;
    private Button _pokerButton = null!;

    private readonly Dictionary<string, List<NarrativeMessageRow>> _messagesByContact = new();
    private readonly Dictionary<string, int> _lastSeenCount = new();
    private readonly List<string> _orderedContactIds = new();
    private readonly List<NarrativeMessageRow> _loadScratch = new();

    private string? _activeContactId;
    private string? _shownFireIdentity;
    private bool _initialized;

    // Dirty-flag identity for the Bank tab's polled labels/meters
    // (ui_conventions.md: no per-frame string formatting) — independent of
    // the Messages tab's _shownFireIdentity above.
    private bool _bankInitialized;
    private double _shownFunds = double.NaN;
    private long _shownDaysUntilBill = -1;
    private int _shownEquipmentQuality = -1;
    private NeedsState _shownNeeds;

    public override void _Ready()
    {
        _contactList = GetNode<ItemList>("PhoneTabs/Messages/MessagesLayout/ContactList");
        _threadHeaderLabel = GetNode<Label>("PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadHeaderLabel");
        _threadContainer = GetNode<VBoxContainer>("PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadScroll/ThreadContainer");
        _choicesContainer = GetNode<VBoxContainer>("PhoneTabs/Messages/MessagesLayout/ThreadPanel/ChoicesContainer");
        _contactList.ItemSelected += OnContactSelected;
        _threadHeaderLabel.Text = NoContactSelectedText;

        _fundsValueLabel = GetNode<Label>("PhoneTabs/Bank/BankScroll/BankLayout/FundsCard/FundsCardLayout/FundsValueLabel");
        _costOfLivingLabel = GetNode<Label>("PhoneTabs/Bank/BankScroll/BankLayout/FundsCard/FundsCardLayout/CostOfLivingLabel");
        _equipmentTierLabel = GetNode<Label>("PhoneTabs/Bank/BankScroll/BankLayout/EquipmentCard/EquipmentCardLayout/EquipmentTierLabel");
        _equipmentNextLabel = GetNode<Label>("PhoneTabs/Bank/BankScroll/BankLayout/EquipmentCard/EquipmentCardLayout/EquipmentNextLabel");
        _hungerBar = GetNode<ProgressBar>("PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HungerRow/HungerBar");
        _sleepBar = GetNode<ProgressBar>("PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SleepRow/SleepBar");
        _hygieneBar = GetNode<ProgressBar>("PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HygieneRow/HygieneBar");
        _socialBar = GetNode<ProgressBar>("PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SocialRow/SocialBar");
        _fitnessBar = GetNode<ProgressBar>("PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/FitnessRow/FitnessBar");
        _narcoticsButton = GetNode<Button>("PhoneTabs/Bank/BankScroll/BankLayout/HustlesCard/HustlesCardLayout/HustleButtonsRow/NarcoticsButton");
        _fencingButton = GetNode<Button>("PhoneTabs/Bank/BankScroll/BankLayout/HustlesCard/HustlesCardLayout/HustleButtonsRow/FencingButton");
        _pokerButton = GetNode<Button>("PhoneTabs/Bank/BankScroll/BankLayout/HustlesCard/HustlesCardLayout/HustleButtonsRow/PokerButton");
        _narcoticsButton.Pressed += OnNarcoticsPressed;
        _fencingButton.Pressed += OnFencingPressed;
        _pokerButton.Pressed += OnPokerPressed;
    }

    public override void _ExitTree()
    {
        _contactList.ItemSelected -= OnContactSelected;
        _narcoticsButton.Pressed -= OnNarcoticsPressed;
        _fencingButton.Pressed -= OnFencingPressed;
        _pokerButton.Pressed -= OnPokerPressed;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }

        RefreshBankTab(gm);

        bool hasPending = gm.GrittyEventChoices.TryGetPendingChoice(out PendingGrittyChoice pending);
        string? identity = hasPending
            ? $"{pending.Fired.EventId}|{pending.Fired.SubjectPlayerId}|{pending.Fired.Day}"
            : null;

        if (_initialized && identity == _shownFireIdentity)
        {
            return;
        }
        _initialized = true;
        _shownFireIdentity = identity;

        ReloadMessages(gm);
        string? pendingContactId = hasPending ? pending.Definition.ContactId : null;

        // Mark-read before the relabel below, so a freshly auto-opened
        // pending thread's unread dot clears in the same pass instead of
        // lingering until the next transition.
        if (hasPending)
        {
            MarkRead(pendingContactId!, pendingContactId);
        }
        RefreshContactList(gm, pendingContactId);

        if (hasPending)
        {
            RenderThread(gm, pendingContactId!, pending);
        }
        else if (_activeContactId is not null)
        {
            RenderThread(gm, _activeContactId, null);
        }
    }

    private void OnContactSelected(long index)
    {
        if (index < 0 || index >= _orderedContactIds.Count)
        {
            return;
        }
        GameManager gm = GameManager.Instance!;
        string contactId = _orderedContactIds[(int)index];
        bool hasPending = gm.GrittyEventChoices.TryGetPendingChoice(out PendingGrittyChoice pending);
        string? pendingContactId = hasPending ? pending.Definition.ContactId : null;

        MarkRead(contactId, pendingContactId);
        RenderThread(gm, contactId, hasPending ? pending : null);
        // Clearing the unread marker on the item just opened needs the list
        // relabeled; cheap at this scale (a handful of contacts).
        RefreshContactList(gm, pendingContactId);
    }

    private void ReloadMessages(GameManager gm)
    {
        gm.NarrativeLog.LoadForPlayer(gm.Career.AvatarPlayerId, _loadScratch);
        _messagesByContact.Clear();
        foreach (NarrativeMessageRow row in _loadScratch)
        {
            if (!_messagesByContact.TryGetValue(row.ContactId, out List<NarrativeMessageRow>? thread))
            {
                thread = new List<NarrativeMessageRow>();
                _messagesByContact[row.ContactId] = thread;
            }
            thread.Add(row);
        }
    }

    private void RefreshContactList(GameManager gm, string? pendingContactId)
    {
        _orderedContactIds.Clear();
        _orderedContactIds.AddRange(_messagesByContact.Keys);
        if (pendingContactId is not null && !_messagesByContact.ContainsKey(pendingContactId))
        {
            _orderedContactIds.Add(pendingContactId);
        }
        _orderedContactIds.Sort((a, b) =>
        {
            bool aPending = a == pendingContactId;
            bool bPending = b == pendingContactId;
            if (aPending != bPending)
            {
                return aPending ? -1 : 1;
            }
            return LastDayFor(b).CompareTo(LastDayFor(a));
        });

        int selectedIndex = -1;
        _contactList.Clear();
        for (int i = 0; i < _orderedContactIds.Count; i++)
        {
            string contactId = _orderedContactIds[i];
            ContactDefinition contact = gm.Contacts.Resolve(contactId);
            bool unread = EffectiveCount(contactId, pendingContactId) > _lastSeenCount.GetValueOrDefault(contactId);
            _contactList.AddItem(unread ? UnreadMarker + contact.DisplayName : contact.DisplayName);
            if (contactId == _activeContactId)
            {
                selectedIndex = i;
            }
        }
        if (selectedIndex >= 0)
        {
            _contactList.Select(selectedIndex);
        }
    }

    private void RenderThread(GameManager gm, string contactId, PendingGrittyChoice? pending)
    {
        _activeContactId = contactId;
        _threadHeaderLabel.Text = gm.Contacts.Resolve(contactId).DisplayName;

        foreach (Node child in _threadContainer.GetChildren())
        {
            child.QueueFree();
        }
        foreach (Node child in _choicesContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (_messagesByContact.TryGetValue(contactId, out List<NarrativeMessageRow>? thread))
        {
            foreach (NarrativeMessageRow row in thread)
            {
                int dayOfSeason = GlobalState.DayOfSeasonForDay(row.GameDay);
                AddBubble(row.Prompt, incoming: true, string.Format(TimestampFormat, row.SeasonYear, dayOfSeason));
                AddBubble(row.Choice, incoming: false, null);
            }
        }

        if (pending is { } liveFire && liveFire.Definition.ContactId == contactId)
        {
            AddBubble(liveFire.Definition.Prompt, incoming: true, null);
            EventChoice[] eventChoices = liveFire.Definition.Choices;
            for (int i = 0; i < eventChoices.Length; i++)
            {
                int choiceIndex = i; // captured by value, not the loop variable
                var button = new Button { Text = eventChoices[i].Label };
                button.Pressed += () => GameManager.Instance!.GrittyEventChoices.ResolveChoice(choiceIndex);
                _choicesContainer.AddChild(button);
            }
        }
    }

    private void AddBubble(string text, bool incoming, string? caption)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(240, 0),
        };
        var inner = new VBoxContainer();
        inner.AddChild(label);
        if (!string.IsNullOrEmpty(caption))
        {
            inner.AddChild(new Label { Text = caption, ThemeTypeVariation = "CaptionLabel" });
        }

        var bubble = new PanelContainer
        {
            ThemeTypeVariation = incoming ? "MessageBubbleIncoming" : "MessageBubbleOutgoing",
        };
        bubble.AddChild(inner);

        var row = new HBoxContainer();
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (incoming)
        {
            row.AddChild(bubble);
            row.AddChild(spacer);
        }
        else
        {
            row.AddChild(spacer);
            row.AddChild(bubble);
        }
        _threadContainer.AddChild(row);
    }

    private long LastDayFor(string contactId) =>
        _messagesByContact.TryGetValue(contactId, out List<NarrativeMessageRow>? thread) && thread.Count > 0
            ? thread[^1].GameDay
            : long.MinValue;

    private int EffectiveCount(string contactId, string? pendingContactId)
    {
        int count = _messagesByContact.TryGetValue(contactId, out List<NarrativeMessageRow>? thread) ? thread.Count : 0;
        return contactId == pendingContactId ? count + 1 : count;
    }

    private void MarkRead(string contactId, string? pendingContactId) =>
        _lastSeenCount[contactId] = EffectiveCount(contactId, pendingContactId);

    private void RefreshBankTab(GameManager gm)
    {
        string avatarId = gm.Career.AvatarPlayerId;

        if (!gm.LifeSim.TryGetFunds(avatarId, out double funds))
        {
            funds = 0.0;
        }
        long sinceBill = gm.State.CurrentDay % LifeSimManager.CostOfLivingCadenceDays;
        long daysUntilBill = sinceBill == 0
            ? LifeSimManager.CostOfLivingCadenceDays
            : LifeSimManager.CostOfLivingCadenceDays - sinceBill;
        if (!_bankInitialized || funds != _shownFunds || daysUntilBill != _shownDaysUntilBill)
        {
            _shownFunds = funds;
            _shownDaysUntilBill = daysUntilBill;
            _fundsValueLabel.Text = string.Format(FundsFormat, funds);
            _costOfLivingLabel.Text = string.Format(CostOfLivingFormat, LifeSimManager.WeeklyCostOfLiving, daysUntilBill);
        }

        int equipmentQuality = gm.Gear.QualityFor(avatarId);
        if (!_bankInitialized || equipmentQuality != _shownEquipmentQuality)
        {
            _shownEquipmentQuality = equipmentQuality;
            _equipmentTierLabel.Text = string.Format(EquipmentTierFormat, TierNames[equipmentQuality]);
            _equipmentNextLabel.Text = equipmentQuality >= TierNames.Length - 1
                ? TopTierOwnedText
                : string.Format(EquipmentNextFormat, TierNames[equipmentQuality + 1], EquipmentService.PriceForQuality(equipmentQuality + 1));
        }

        if (!gm.LifeSim.TryGetNeeds(avatarId, out NeedsState needs))
        {
            needs = NeedsState.FullySatisfied();
        }
        if (!_bankInitialized || !NeedsEqual(needs, _shownNeeds))
        {
            _shownNeeds = needs;
            _hungerBar.Value = needs.Hunger;
            _sleepBar.Value = needs.Sleep;
            _hygieneBar.Value = needs.Hygiene;
            _socialBar.Value = needs.Social;
            _fitnessBar.Value = needs.Fitness;
        }

        _bankInitialized = true;
    }

    private static bool NeedsEqual(in NeedsState a, in NeedsState b) =>
        a.Hunger == b.Hunger && a.Sleep == b.Sleep && a.Hygiene == b.Hygiene
        && a.Social == b.Social && a.Fitness == b.Fitness;

    private void OnNarcoticsPressed() => EmitSignal(SignalName.HustleLaunchRequested, (int)WorkActivity.Narcotics);

    private void OnFencingPressed() => EmitSignal(SignalName.HustleLaunchRequested, (int)WorkActivity.Fencing);

    private void OnPokerPressed() => EmitSignal(SignalName.HustleLaunchRequested, (int)WorkActivity.Poker);
}
