using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Equipment;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Economy.Items;
using DirtAndDiamonds.Economy.Phone;
using DirtAndDiamonds.Narrative.Contacts;
using DirtAndDiamonds.Narrative.Events;
using DirtAndDiamonds.Simulation.Life;
using DirtAndDiamonds.UI.Portraits;
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

    /// <summary>
    /// 12b: emitted when the Bank tab's Gear-card "Open Pro Shop" button is
    /// pressed. Main bridges it to the EquipmentShopScreen modal — the same
    /// shared-ancestor seam as HustleLaunchRequested above, since the shop
    /// overlay lives outside the phone's subtree.
    /// </summary>
    [Signal]
    public delegate void ShopOpenRequestedEventHandler();

    [Export]
    public string TimestampFormat { get; set; } = "Season {0}, day {1}";

    // '•' (U+2022) rather than '●': the vendored Barlow faces cover Latin
    // punctuation but not the geometric-shapes block.
    [Export]
    public string UnreadMarker { get; set; } = "• ";

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

    /// <summary>HS-3 §4.1: hardware-tier names indexed by tier-1 (1=Burner .. 3=Flagship), mirroring NewGameScreen's own copy.</summary>
    [Export]
    public string[] PhoneTierNames { get; set; } =
    {
        "Burner", "Mid-Tier", "Flagship",
    };

    [Export]
    public string MarketplaceLockedHintFormat { get; set; } = "Upgrade to a {0} phone to unlock the Marketplace.";

    [Export]
    public string MarketplaceItemFormat { get; set; } = "{0} — ${1:F0}";

    [Export]
    public string MarketplaceBuyFormat { get; set; } = "Buy ${0:F0}";

    [Export]
    public string MarketplaceOwnedText { get; set; } = "Owned";

    [Export]
    public string MarketplacePurchasedFormat { get; set; } = "Bought {0}.";

    [Export]
    public string MarketplaceInsufficientFundsText { get; set; } = "Not enough cash for that.";

    [Export]
    public string MarketplaceAlreadyOwnedText { get; set; } = "You already own that.";

    /// <summary>HS-3 §4.2: the tab is minute-gated (not tier-gated) — metered plan, no Wi-Fi, balance below the browse cost.</summary>
    [Export]
    public string MarketplaceNoMinutesHintFormat { get; set; } = "Browsing costs {0} min — buy minutes at the carrier (Bank tab).";

    /// <summary>Plan display names indexed by Phone_State.plan 0–2.</summary>
    [Export]
    public string[] PhonePlanNames { get; set; } =
    {
        "Prepaid", "Basic", "Unlimited",
    };

    [Export]
    public string PhoneStatusFormat { get; set; } = "{0} phone — {1} plan";

    [Export]
    public string MinutesRemainingFormat { get; set; } = "{0} min left";

    [Export]
    public string UnlimitedMinutesText { get; set; } = "Unlimited minutes";

    [Export]
    public string HomeWifiText { get; set; } = "Home Wi-Fi — phone actions are free at home.";

    [Export]
    public string NoHomeWifiText { get; set; } = "No home Wi-Fi — minutes are metered.";

    [Export]
    public string BuyMinutesFormat { get; set; } = "Buy {0} min — ${1:F0}";

    [Export]
    public string UpgradePhoneFormat { get; set; } = "Upgrade: {0} — ${1:F0}";

    [Export]
    public string TopPhoneOwnedText { get; set; } = "Flagship owned";

    [Export]
    public string CarrierBoughtMinutesFormat { get; set; } = "Added {0} minutes.";

    [Export]
    public string CarrierUpgradedFormat { get; set; } = "Upgraded to the {0}.";

    [Export]
    public string CarrierInsufficientFundsText { get; set; } = "Not enough cash for that.";

    private ItemList _contactList = null!;
    private PortraitView _threadPortrait = null!;
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
    private Button _proShopButton = null!;

    private TabContainer _phoneTabs = null!;
    private Label _marketplaceFundsLabel = null!;
    private VBoxContainer _itemsContainer = null!;
    private Label _marketplaceStatusLabel = null!;
    private int _marketplaceTabIndex = -1;

    private PanelContainer _carrierCard = null!;
    private Label _phoneStatusLabel = null!;
    private Label _minutesLabel = null!;
    private Label _wifiLabel = null!;
    private Button _buyMinutesButton = null!;
    private Button _upgradePhoneButton = null!;
    private Label _carrierStatusLabel = null!;

    private readonly Dictionary<string, Button> _marketplaceBuyButtons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ownedItemIds = new(StringComparer.Ordinal);
    private readonly List<PlayerItemRow> _ownedItemsScratch = new();

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

    // Dirty-flag identity for the Marketplace tab — independent of the Bank
    // tab's above since either can move without the other (funds move for
    // both, but the phone tier/minutes only gate this one).
    private bool _marketplaceInitialized;
    private string _shownMarketplaceTooltip = "\0"; // impossible tooltip forces the first pass
    private double _shownMarketplaceFunds = double.NaN;

    // HS-3 §4.2: Phone_State / home-Wi-Fi / owned items are DB rows with no
    // in-memory mirror, so they are snapshotted here and re-read only when
    // the day changes (the §3.2 family tick — the only other writer — runs on
    // the day tick) or after this screen's own carrier/marketplace action
    // invalidates the snapshot. Never a per-frame query.
    private bool _hasPhoneRow;
    private PhoneStateRow _phoneRow;
    private bool _homeWifi;
    private long _phoneLoadedDay = long.MinValue;
    private long _ownedLoadedDay = long.MinValue;
    private bool _carrierDirty = true;
    private double _shownCarrierFunds = double.NaN;

    public override void _Ready()
    {
        _contactList = GetNode<ItemList>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ContactList");
        _threadPortrait = GetNode<PortraitView>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadHeaderRow/ThreadPortrait");
        _threadHeaderLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadHeaderRow/ThreadHeaderLabel");
        _threadContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadScroll/ThreadContainer");
        _choicesContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ThreadPanel/ChoicesContainer");
        _contactList.ItemSelected += OnContactSelected;
        _threadHeaderLabel.Text = NoContactSelectedText;

        _fundsValueLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/FundsCard/FundsCardLayout/FundsValueLabel");
        _costOfLivingLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/FundsCard/FundsCardLayout/CostOfLivingLabel");
        _equipmentTierLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/EquipmentCard/EquipmentCardLayout/EquipmentTierLabel");
        _equipmentNextLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/EquipmentCard/EquipmentCardLayout/EquipmentNextLabel");
        _hungerBar = GetNode<ProgressBar>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HungerRow/HungerBar");
        _sleepBar = GetNode<ProgressBar>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SleepRow/SleepBar");
        _hygieneBar = GetNode<ProgressBar>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HygieneRow/HygieneBar");
        _socialBar = GetNode<ProgressBar>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SocialRow/SocialBar");
        _fitnessBar = GetNode<ProgressBar>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/FitnessRow/FitnessBar");
        _narcoticsButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/HustlesCard/HustlesCardLayout/HustleButtonsRow/NarcoticsButton");
        _fencingButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/HustlesCard/HustlesCardLayout/HustleButtonsRow/FencingButton");
        _pokerButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/HustlesCard/HustlesCardLayout/HustleButtonsRow/PokerButton");
        _proShopButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/EquipmentCard/EquipmentCardLayout/ProShopButton");
        _narcoticsButton.Pressed += OnNarcoticsPressed;
        _fencingButton.Pressed += OnFencingPressed;
        _pokerButton.Pressed += OnPokerPressed;
        _proShopButton.Pressed += OnProShopPressed;

        _carrierCard = GetNode<PanelContainer>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/CarrierCard");
        _phoneStatusLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/CarrierCard/CarrierCardLayout/PhoneStatusLabel");
        _minutesLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/CarrierCard/CarrierCardLayout/MinutesLabel");
        _wifiLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/CarrierCard/CarrierCardLayout/WifiLabel");
        _buyMinutesButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/CarrierCard/CarrierCardLayout/CarrierButtonsRow/BuyMinutesButton");
        _upgradePhoneButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/CarrierCard/CarrierCardLayout/CarrierButtonsRow/UpgradePhoneButton");
        _carrierStatusLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/CarrierCard/CarrierCardLayout/CarrierStatusLabel");
        _buyMinutesButton.Pressed += OnBuyMinutesPressed;
        _upgradePhoneButton.Pressed += OnUpgradePhonePressed;

        _phoneTabs = GetNode<TabContainer>("Screen/ScreenLayout/PhoneTabs");
        Control marketplaceTab = GetNode<Control>("Screen/ScreenLayout/PhoneTabs/Marketplace");
        _marketplaceTabIndex = marketplaceTab.GetIndex();
        _marketplaceFundsLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Marketplace/MarketplaceScroll/MarketplaceLayout/MarketplaceCard/MarketplaceCardLayout/MarketplaceFundsLabel");
        _itemsContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Marketplace/MarketplaceScroll/MarketplaceLayout/MarketplaceCard/MarketplaceCardLayout/ItemsContainer");
        _marketplaceStatusLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Marketplace/MarketplaceScroll/MarketplaceLayout/MarketplaceCard/MarketplaceCardLayout/MarketplaceStatusLabel");
        _phoneTabs.TabChanged += OnPhoneTabChanged;
        BuildMarketplaceRows();
    }

    public override void _ExitTree()
    {
        _contactList.ItemSelected -= OnContactSelected;
        _narcoticsButton.Pressed -= OnNarcoticsPressed;
        _fencingButton.Pressed -= OnFencingPressed;
        _pokerButton.Pressed -= OnPokerPressed;
        _proShopButton.Pressed -= OnProShopPressed;
        _buyMinutesButton.Pressed -= OnBuyMinutesPressed;
        _upgradePhoneButton.Pressed -= OnUpgradePhonePressed;
        _phoneTabs.TabChanged -= OnPhoneTabChanged;
    }

    /// <summary>
    /// One row per catalog entry, built once from <c>GameManager.Items</c>
    /// (§5's authoring order IS the marketplace listing order per
    /// ItemCatalog's own doc comment) with a heading whenever the category
    /// changes. Content-driven, so this never hardcodes an item — a new
    /// items.json entry just shows up here on the next boot.
    /// </summary>
    private void BuildMarketplaceRows()
    {
        ItemCatalog catalog = GameManager.Instance!.Items;
        ItemCategory? lastCategory = null;
        foreach (ItemDefinition item in catalog.Entries)
        {
            if (item.Category != lastCategory)
            {
                lastCategory = item.Category;
                _itemsContainer.AddChild(new Label
                {
                    Text = item.Category.ToString(),
                    ThemeTypeVariation = "HeadingLabel",
                });
            }

            var nameLabel = new Label
            {
                Text = string.Format(MarketplaceItemFormat, item.Name, item.Price),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            var buyButton = new Button { Text = string.Format(MarketplaceBuyFormat, item.Price) };
            string itemId = item.Id; // captured by value, not the loop variable
            buyButton.Pressed += () => OnBuyItemPressed(itemId);

            var row = new HBoxContainer();
            row.AddChild(nameLabel);
            row.AddChild(buyButton);
            _itemsContainer.AddChild(row);
            _marketplaceBuyButtons[itemId] = buyButton;
        }
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }

        RefreshPhoneSnapshot(gm);
        RefreshBankTab(gm);
        RefreshMarketplaceTab(gm);

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
        ContactDefinition contact = gm.Contacts.Resolve(contactId);
        _threadHeaderLabel.Text = contact.DisplayName;
        _threadPortrait.SetIdentity(contact.PortraitKey, contact.DisplayName);

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
                var button = new Button
                {
                    Text = eventChoices[i].Label,
                    ThemeTypeVariation = "ReplyChip",
                };
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

        RefreshCarrierCard(funds);
        _bankInitialized = true;
    }

    /// <summary>
    /// Re-snapshots Phone_State + home Wi-Fi once per day change (or after a
    /// carrier action flags the snapshot stale) — the family tick is the only
    /// other Phone_State writer and it runs on the day tick, so a day-scoped
    /// cache is exact. This replaced a per-frame TryGetPhone query.
    /// </summary>
    private void RefreshPhoneSnapshot(GameManager gm)
    {
        long day = gm.State.CurrentDay;
        if (day == _phoneLoadedDay)
        {
            return;
        }
        _phoneLoadedDay = day;
        string avatarId = gm.Career.AvatarPlayerId;
        _hasPhoneRow = gm.Persons.TryGetPhone(avatarId, out _phoneRow);
        _homeWifi = gm.Phone.IsOnHomeWifi(avatarId);
        _carrierDirty = true;
    }

    /// <summary>
    /// The Bank tab's carrier card (§4.2): phone/plan status, minutes, Wi-Fi
    /// note, the $10→100min bundle button, and the hardware-upgrade rung.
    /// Hidden outright on a pre-v11 save (no Phone_State row = nothing
    /// metered, nothing to sell). Sold here, not in the Marketplace, so a
    /// tier-1 burner kid can always reach the upgrade (see PhoneService).
    /// </summary>
    private void RefreshCarrierCard(double funds)
    {
        if (!_hasPhoneRow)
        {
            if (_carrierDirty)
            {
                _carrierCard.Visible = false;
                _carrierDirty = false;
            }
            return;
        }
        if (!_carrierDirty && funds == _shownCarrierFunds)
        {
            return;
        }
        _carrierDirty = false;
        _shownCarrierFunds = funds;

        _carrierCard.Visible = true;
        _phoneStatusLabel.Text = string.Format(
            PhoneStatusFormat, PhoneTierNames[_phoneRow.Tier - 1], PhonePlanNames[_phoneRow.Plan]);
        _minutesLabel.Text = _phoneRow.Plan == PhoneService.UnlimitedPlan
            ? UnlimitedMinutesText
            : string.Format(MinutesRemainingFormat, _phoneRow.MinutesRemaining);
        _wifiLabel.Text = _homeWifi ? HomeWifiText : NoHomeWifiText;

        bool metered = _phoneRow.Plan != PhoneService.UnlimitedPlan;
        _buyMinutesButton.Visible = metered;
        if (metered)
        {
            _buyMinutesButton.Text = string.Format(
                BuyMinutesFormat, PhoneService.BundleMinutes, PhoneService.BundlePriceDollars);
            _buyMinutesButton.Disabled = funds < PhoneService.BundlePriceDollars;
        }

        if (_phoneRow.Tier >= PhoneService.FlagshipTier)
        {
            _upgradePhoneButton.Text = TopPhoneOwnedText;
            _upgradePhoneButton.Disabled = true;
        }
        else
        {
            double price = PhoneService.PriceForTier(_phoneRow.Tier + 1);
            _upgradePhoneButton.Text = string.Format(
                UpgradePhoneFormat, PhoneTierNames[_phoneRow.Tier], price);
            _upgradePhoneButton.Disabled = funds < price;
        }
    }

    private void OnBuyMinutesPressed()
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }
        try
        {
            bool bought = gm.Phone.TryBuyBundle(gm.Career.AvatarPlayerId, out PhoneActionFailure failure);
            _carrierStatusLabel.Text = bought
                ? string.Format(CarrierBoughtMinutesFormat, PhoneService.BundleMinutes)
                : failure == PhoneActionFailure.InsufficientFunds
                    ? CarrierInsufficientFundsText
                    : failure.ToString();
        }
        catch (Exception ex)
        {
            _carrierStatusLabel.Text = ex.Message;
        }
        _carrierStatusLabel.Visible = true;
        _phoneLoadedDay = long.MinValue; // re-snapshot next frame
    }

    private void OnUpgradePhonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }
        try
        {
            bool upgraded = gm.Phone.TryUpgradePhone(
                gm.Career.AvatarPlayerId, gm.State.CurrentDay, out PhoneActionFailure failure);
            _carrierStatusLabel.Text = upgraded
                ? string.Format(CarrierUpgradedFormat, PhoneTierNames[_phoneRow.Tier]) // _phoneRow is the pre-upgrade snapshot; [Tier] IS the new rung's 0-based name index
                : failure == PhoneActionFailure.InsufficientFunds
                    ? CarrierInsufficientFundsText
                    : failure.ToString();
        }
        catch (Exception ex)
        {
            _carrierStatusLabel.Text = ex.Message;
        }
        _carrierStatusLabel.Visible = true;
        _phoneLoadedDay = long.MinValue; // re-snapshot next frame
    }

    /// <summary>
    /// §4.2: entering the Marketplace tab is a browse session (3 min). The
    /// spend itself applies every bypass (no row / Unlimited / Wi-Fi), so
    /// this only invalidates the snapshot when something could have been
    /// written. The Messages tab is deliberately absent here — §4.3: reading
    /// and answering event threads never costs a minute.
    /// </summary>
    private void OnPhoneTabChanged(long tabIndex)
    {
        if ((int)tabIndex != _marketplaceTabIndex || !_hasPhoneRow)
        {
            return;
        }
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }
        if (_phoneRow.Plan == PhoneService.UnlimitedPlan || _homeWifi)
        {
            return; // free browse, nothing written
        }
        gm.Phone.TrySpendMinutes(
            gm.Career.AvatarPlayerId, PhoneService.MarketplaceBrowseMinuteCost, onWifi: false, out _);
        _phoneLoadedDay = long.MinValue; // re-snapshot next frame
    }

    private static bool NeedsEqual(in NeedsState a, in NeedsState b) =>
        a.Hunger == b.Hunger && a.Sleep == b.Sleep && a.Hygiene == b.Hygiene
        && a.Social == b.Social && a.Fitness == b.Fitness;

    /// <summary>
    /// §4.1 tier gating + §4.2 minutes gating: the tab itself stays visible
    /// but is disabled with a tooltip hint, never hidden outright — below
    /// phone tier 2 (Mid) the hint sells the upgrade; on a metered plan with
    /// no Wi-Fi and a balance under the browse cost it points at the carrier.
    /// No Phone_State row means the pre-v11 phone — every feature unlocked,
    /// nothing metered (Phone_State's own schema comment). The Messages tab
    /// is never gated by any of this (§4.3). Owned items reload once per day
    /// (the §3.2 autobuy tick can gift on the day tick), plus the local add
    /// on this screen's own purchase.
    /// </summary>
    private void RefreshMarketplaceTab(GameManager gm)
    {
        string avatarId = gm.Career.AvatarPlayerId;

        if (gm.State.CurrentDay != _ownedLoadedDay)
        {
            _ownedLoadedDay = gm.State.CurrentDay;
            gm.Persons.LoadItemsFor(avatarId, _ownedItemsScratch);
            _ownedItemIds.Clear();
            foreach (PlayerItemRow owned in _ownedItemsScratch)
            {
                _ownedItemIds.Add(owned.ItemId);
            }
            _shownMarketplaceFunds = double.NaN; // re-evaluate the buy buttons below
        }

        bool tierLocked = _hasPhoneRow && _phoneRow.Tier < PhoneService.MidTier;
        bool minutesLocked = !tierLocked && _hasPhoneRow
            && _phoneRow.Plan != PhoneService.UnlimitedPlan && !_homeWifi
            && _phoneRow.MinutesRemaining < PhoneService.MarketplaceBrowseMinuteCost;
        bool locked = tierLocked || minutesLocked;
        string tooltip = tierLocked
            ? string.Format(MarketplaceLockedHintFormat, PhoneTierNames[1])
            : minutesLocked
                ? string.Format(MarketplaceNoMinutesHintFormat, PhoneService.MarketplaceBrowseMinuteCost)
                : string.Empty;
        if (!_marketplaceInitialized || tooltip != _shownMarketplaceTooltip)
        {
            _shownMarketplaceTooltip = tooltip;
            _phoneTabs.SetTabDisabled(_marketplaceTabIndex, locked);
            _phoneTabs.SetTabTooltip(_marketplaceTabIndex, tooltip);
        }
        if (locked)
        {
            _marketplaceInitialized = true;
            return; // nothing to browse while the tab can't be opened
        }

        if (!gm.LifeSim.TryGetFunds(avatarId, out double funds))
        {
            funds = 0.0;
        }
        if (!_marketplaceInitialized || funds != _shownMarketplaceFunds)
        {
            _shownMarketplaceFunds = funds;
            _marketplaceFundsLabel.Text = string.Format(FundsFormat, funds);
            foreach (KeyValuePair<string, Button> pair in _marketplaceBuyButtons)
            {
                RefreshBuyButton(pair.Key, pair.Value, funds);
            }
        }
        _marketplaceInitialized = true;
    }

    private void RefreshBuyButton(string itemId, Button button, double funds)
    {
        if (_ownedItemIds.Contains(itemId))
        {
            button.Disabled = true;
            button.Text = MarketplaceOwnedText;
            return;
        }
        button.Disabled = funds < GameManager.Instance!.Items.Require(itemId).Price;
    }

    private void OnBuyItemPressed(string itemId)
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }
        string avatarId = gm.Career.AvatarPlayerId;
        try
        {
            bool bought = gm.ItemShop.TryPurchase(avatarId, itemId, gm.State.CurrentDay, out ItemPurchaseFailure failure);
            if (bought)
            {
                _ownedItemIds.Add(itemId);
                if (_marketplaceBuyButtons.TryGetValue(itemId, out Button? button))
                {
                    button.Disabled = true;
                    button.Text = MarketplaceOwnedText;
                }
                _marketplaceStatusLabel.Text = string.Format(MarketplacePurchasedFormat, gm.Items.Require(itemId).Name);
            }
            else
            {
                _marketplaceStatusLabel.Text = failure switch
                {
                    ItemPurchaseFailure.InsufficientFunds => MarketplaceInsufficientFundsText,
                    ItemPurchaseFailure.AlreadyOwned => MarketplaceAlreadyOwnedText,
                    _ => failure.ToString(),
                };
            }
        }
        catch (Exception ex)
        {
            _marketplaceStatusLabel.Text = ex.Message;
        }
        _marketplaceStatusLabel.Visible = true;
    }

    private void OnNarcoticsPressed() => EmitSignal(SignalName.HustleLaunchRequested, (int)WorkActivity.Narcotics);

    private void OnFencingPressed() => EmitSignal(SignalName.HustleLaunchRequested, (int)WorkActivity.Fencing);

    private void OnPokerPressed() => EmitSignal(SignalName.HustleLaunchRequested, (int)WorkActivity.Poker);

    private void OnProShopPressed() => EmitSignal(SignalName.ShopOpenRequested);
}
