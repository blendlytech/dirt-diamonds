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
/// Phase 10b, split into Events/Messages tabs in the phone-split slice
/// (docs/progress.md's two 2026-07-10 "Events vs Messages" entries): the
/// right panel of the two-panel shell. The Events tab is a single
/// chronological feed over the narrative-log's Event-kind rows — scene-prose
/// cards (day/season, contact, prompt, resolution line), with the avatar's
/// pending gritty-event choice rendered as the last (unanswered) card plus
/// reply-chip buttons underneath (<see cref="EventConsequenceApplier.TryGetPendingChoice"/>
/// /<see cref="EventConsequenceApplier.ResolveChoice"/>). The Messages tab is
/// the companion-text read-model — a contact list (most-recent-first, unread
/// marker) and the selected contact's thread of real texts (Text-kind rows
/// only), never carrying event prose or the choice UI (§4.3: narrative never
/// gates either tab — no minute cost, no tier lock). UI never touches the
/// database directly: history comes from <see cref="GameManager.NarrativeLog"/>'s
/// read-back, reloaded on a pending-fire transition OR a day change (never
/// per-frame — a day change alone can deliver a previously-invisible delayed
/// text), matching BaseballDashboard's dirty-flag discipline. Node paths
/// verified against BurnerPhone.tscn (authored alongside this script) via
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

    /// <summary>
    /// Onboarding-2 T-2: emitted when the Settings tab's "Replay tutorial"
    /// button is pressed. Main bridges it to TutorialOverlay.Open(0) — same
    /// shared-ancestor seam as ShopOpenRequested, since the overlay is a
    /// permanent sibling outside the phone's subtree.
    /// </summary>
    [Signal]
    public delegate void TutorialReplayRequestedEventHandler();

    [Export]
    public string TimestampFormat { get; set; } = "Season {0}, day {1}";

    // '•' (U+2022) rather than '●': the vendored Barlow faces cover Latin
    // punctuation but not the geometric-shapes block.
    [Export]
    public string UnreadMarker { get; set; } = "• ";

    [Export]
    public string NoContactSelectedText { get; set; } = "Select a contact";

    /// <summary>The Events feed's fallback resolution line when a choice ships no authored "outcome" — every pre-split event degrades to this.</summary>
    [Export]
    public string OutcomeFallbackFormat { get; set; } = "You: {0}";

    /// <summary>
    /// Events feed card headings, indexed by <see cref="EventCategory"/>'s
    /// ordinal (the TierNames/PhoneTierNames convention already used
    /// elsewhere in this file) — a scene category ("Baseball", "Family",
    /// "Romance"...) instead of the contact's name, so an Events card reads
    /// as a genuinely different surface than a Messages thread rather than
    /// both showing "Mom"/"Coach Malone" as the heading.
    /// </summary>
    [Export]
    public string[] CategoryLabels { get; set; } =
    {
        "Baseball", "Family", "Romance", "School", "Hustle", "Career", "General",
    };

    /// <summary>
    /// Closes the disclosed seam: the Events feed used to rebuild a Control
    /// card for every Event-kind row ever logged, every reload (day-change
    /// cadence) — harmless at playtest scale but unbounded node churn across
    /// a long career. <see cref="ReloadMessages"/> now trims <c>_eventRows</c>
    /// to the most recent <see cref="MaxEventCards"/> before
    /// <see cref="RenderEventsFeed"/> ever sees it; the DB history itself is
    /// untouched, only the rendered feed is bounded.
    /// </summary>
    [Export]
    public int MaxEventCards { get; set; } = 200;

    /// <summary>History tab: Event-kind rows fetched per "Load Older" click (or the tab's first visit), paging strictly backward from the newest logged event via <see cref="NarrativeLogQueries.LoadEventPageBefore"/>.</summary>
    [Export]
    public int HistoryPageSize { get; set; } = 50;

    /// <summary>Shown in place of the "Load Older" button once paging has reached this player's very first logged event.</summary>
    [Export]
    public string HistoryBeginningText { get; set; } = "— beginning of your story —";

    /// <summary>Status-bar clock: {0}=weekday, {1}=month (both trimmed to three letters, uppercased to match the bar's other captions), {2}=day of month, {3}=hour (12h), {4}=minute, {5}=AM/PM.</summary>
    [Export]
    public string ClockFormat { get; set; } = "{0} {1} {2} · {3}:{4:D2} {5}";

    [Export]
    public string ClockAmText { get; set; } = "AM";

    [Export]
    public string ClockPmText { get; set; } = "PM";

    /// <summary>Settings tab: SaveStatusLabel text after a successful Save Now press ({0}=current day).</summary>
    [Export]
    public string SavedStatusFormat { get; set; } = "Saved ✓ — day {0}";

    /// <summary>Settings tab: SaveStatusLabel text when GameManager.SaveNow reported it couldn't run.</summary>
    [Export]
    public string SaveFailedText { get; set; } = "Couldn't save just now — try again in a moment.";

    [Export]
    public string FundsFormat { get; set; } = "${0:N0}";

    [Export]
    public string CostOfLivingFormat { get; set; } = "Cost of living: ${0:F0}/wk — next bill in {1}d";

    // Onboarding-overlay doc §4.1: the caption naming which need(s) crossed
    // NeedsEngine.CriticalThreshold — the literal behavior of the sim's
    // crisis branch, surfaced instead of invisible.
    [Export]
    public string NeedCriticalFormat { get; set; } =
        "{0} is critical — your player will drop what he's doing to fix it.";

    [Export]
    public string NeedsCriticalPluralFormat { get; set; } =
        "{0} are critical — your player will drop what he's doing to fix them.";

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

    [Export]
    public string NoChildrenText { get; set; } = "No children yet.";

    [Export]
    public string ChildCardHeadingFormat { get; set; } = "{0} (age {1})";

    [Export]
    public string CareLabelText { get; set; } = "Care";

    [Export]
    public string CoachingLabelText { get; set; } = "Coaching";

    [Export]
    public string FundingLabelText { get; set; } = "Funding";

    [Export]
    public string NeglectLabelText { get; set; } = "Neglect";

    [Export]
    public string CommitmentValueFormat { get; set; } = "${0}";

    private ScrollContainer _eventsScroll = null!;
    private VBoxContainer _eventsContainer = null!;
    private VBoxContainer _choicesContainer = null!;

    private Button _loadOlderButton = null!;
    private Label _historyStatusLabel = null!;
    private ScrollContainer _historyScroll = null!;
    private VBoxContainer _historyContainer = null!;
    private int _historyTabIndex = -1;

    // History tab paging state. The tab is a read-only archive (§ design:
    // "History" follow-up to the disclosed feed-cap seam) — it never
    // dirty-flag-refreshes per day like the live Events tab; it only changes
    // on an explicit "Load Older" click (or the tab's first visit). The
    // cursor starts at long.MaxValue (page backward from the newest logged
    // event) and walks toward this player's oldest row one click at a time.
    private long _historyCursor = long.MaxValue;
    private bool _historyReachedBeginning;
    private readonly List<NarrativeMessageRow> _historyPageScratch = new();

    private ItemList _contactList = null!;
    private PortraitView _threadPortrait = null!;
    private Label _threadHeaderLabel = null!;
    private VBoxContainer _threadContainer = null!;

    private Label _fundsValueLabel = null!;
    private Label _costOfLivingLabel = null!;
    private Label _equipmentTierLabel = null!;
    private Label _equipmentNextLabel = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _sleepBar = null!;
    private ProgressBar _hygieneBar = null!;
    private ProgressBar _socialBar = null!;
    private ProgressBar _fitnessBar = null!;
    private Label _hungerLabel = null!;
    private Label _sleepLabel = null!;
    private Label _hygieneLabel = null!;
    private Label _socialLabel = null!;
    private Label _fitnessLabel = null!;
    private Label _needsCriticalCaptionLabel = null!;
    private Button _narcoticsButton = null!;
    private Button _fencingButton = null!;
    private Button _pokerButton = null!;
    private Button _proShopButton = null!;

    // Status-bar clock: the human calendar date + live time-of-day (Slice
    // G-3, read off the same GameManager.TimeOfDay the TimeControlBar
    // drives), complementing the shell top bar's sim day counter.
    // Dirty-flagged on the (day, minute) pair.
    private Label _clockLabel = null!;
    private long _shownClockDay = long.MinValue;
    private int _shownClockMinute = -1;

    private TabContainer _phoneTabs = null!;
    private Label _marketplaceFundsLabel = null!;
    private VBoxContainer _itemsContainer = null!;
    private Label _marketplaceStatusLabel = null!;
    private int _marketplaceTabIndex = -1;

    private Label _noChildrenLabel = null!;
    private VBoxContainer _childrenContainer = null!;
    private HSlider _commitmentSlider = null!;
    private Label _commitmentValueLabel = null!;
    private Button _commitmentConfirmButton = null!;

    // Settings tab: Save Now button + its status line (set only on press —
    // event-driven, never per-frame).
    private Button _saveButton = null!;
    private Label _saveStatusLabel = null!;

    // Settings tab: Quit to Desktop, gated behind a confirmation dialog so a
    // stray click can't drop the player out of their session.
    private Button _quitButton = null!;
    private ConfirmationDialog _quitConfirmDialog = null!;

    // Settings tab: Replay tutorial — reopens TutorialOverlay from step 0
    // via Main's bridge (TutorialReplayRequested).
    private Button _replayButton = null!;

    // Settings tab (Slice D-1): SFX volume — a read/write view of
    // UiSfx.Volume. ValueChanged applies to the bus live while dragging;
    // the ConfigFile write debounces to DragEnded.
    private HSlider _volumeSlider = null!;

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

    // Messages tab: Text-kind rows only, grouped by contact.
    private readonly Dictionary<string, List<NarrativeMessageRow>> _messagesByContact = new();
    private readonly Dictionary<string, int> _lastSeenCount = new();
    private readonly List<string> _orderedContactIds = new();

    // Events tab: Event-kind rows only, one flat chronological feed (the
    // read query already orders oldest-first across every contact).
    private readonly List<NarrativeMessageRow> _eventRows = new();

    private readonly List<NarrativeMessageRow> _loadScratch = new();

    private string? _activeContactId;

    // Dirty-flag identity for the Events/Messages reload: the pending fire's
    // components plus the current day, compared raw (ui_conventions.md: no
    // per-frame string formatting in _Process). The day is part of the
    // identity so a delayed companion text that just crossed its delivery
    // day gets picked up on the very day it becomes visible, even with no
    // pending choice in play.
    private bool _shownHasPending;
    private string? _shownEventId;
    private string? _shownSubjectId;
    private long _shownFireDay = -1;
    private long _shownDay = -1;
    private bool _initialized;

    // Dirty-flag identity for the Bank tab's polled labels/meters
    // (ui_conventions.md: no per-frame string formatting) — independent of
    // the Events/Messages tabs' identity components above.
    private bool _bankInitialized;
    private double _shownFunds = double.NaN;
    private long _shownDaysUntilBill = -1;
    private int _shownEquipmentQuality = -1;
    private NeedsState _shownNeeds;

    // Critical-needs presentation identity (onboarding-overlay doc §4.1):
    // one bit per need at/under CriticalThreshold, so the label recolors and
    // the caption rebuild happen only when a need actually crosses the line,
    // not on every hourly needs tick.
    private int _shownCriticalMask = -1;
    private readonly List<string> _criticalNamesScratch = new(5);

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

    // HS-5 §7.1: the Family tab — Child_Development axes and the weekly
    // funding commitment are DB rows with no in-memory mirror, same posture
    // as the Phone_State/owned-items snapshots above, so they're re-read
    // once per day change rather than per-frame.
    private long _familyLoadedDay = long.MinValue;
    private readonly List<PlayerRow> _familyChildrenScratch = new(4);

    public override void _Ready()
    {
        _clockLabel = GetNode<Label>("Screen/ScreenLayout/StatusBar/ClockLabel");
        _eventsScroll = GetNode<ScrollContainer>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/EventsScroll");
        _eventsContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/EventsScroll/EventsContainer");
        _choicesContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/ChoicesContainer");

        Control historyTab = GetNode<Control>("Screen/ScreenLayout/PhoneTabs/History");
        _historyTabIndex = historyTab.GetIndex();
        _loadOlderButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/History/HistoryLayout/LoadOlderButton");
        _historyStatusLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/History/HistoryLayout/HistoryStatusLabel");
        _historyScroll = GetNode<ScrollContainer>("Screen/ScreenLayout/PhoneTabs/History/HistoryLayout/HistoryScroll");
        _historyContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/History/HistoryLayout/HistoryScroll/HistoryContainer");
        _loadOlderButton.Pressed += OnLoadOlderPressed;

        _contactList = GetNode<ItemList>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ContactList");
        _threadPortrait = GetNode<PortraitView>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadHeaderRow/ThreadPortrait");
        _threadHeaderLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadHeaderRow/ThreadHeaderLabel");
        _threadContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadScroll/ThreadContainer");
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
        _hungerLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HungerRow/HungerLabel");
        _sleepLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SleepRow/SleepLabel");
        _hygieneLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HygieneRow/HygieneLabel");
        _socialLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SocialRow/SocialLabel");
        _fitnessLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/FitnessRow/FitnessLabel");
        _needsCriticalCaptionLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/NeedsCriticalCaptionLabel");

        // The 20-line markers are authored at anchor 0.2 in the scene;
        // re-derived here from the live constant so a CriticalThreshold
        // retune can't leave the scene lying about where the crisis
        // branch fires.
        float criticalAnchor = NeedsEngine.CriticalThreshold / NeedsEngine.MaxNeed;
        SetCriticalMarkAnchor(GetNode<ColorRect>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HungerRow/HungerBar/HungerCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SleepRow/SleepBar/SleepCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/HygieneRow/HygieneBar/HygieneCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/SocialRow/SocialBar/SocialCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("Screen/ScreenLayout/PhoneTabs/Bank/BankScroll/BankLayout/NeedsCard/NeedsCardLayout/FitnessRow/FitnessBar/FitnessCriticalMark"), criticalAnchor);
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

        _noChildrenLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Family/FamilyScroll/FamilyLayout/NoChildrenLabel");
        _childrenContainer = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Family/FamilyScroll/FamilyLayout/ChildrenContainer");
        _commitmentSlider = GetNode<HSlider>("Screen/ScreenLayout/PhoneTabs/Family/FamilyScroll/FamilyLayout/CommitmentCard/CommitmentCardLayout/CommitmentRow/CommitmentSlider");
        _commitmentValueLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Family/FamilyScroll/FamilyLayout/CommitmentCard/CommitmentCardLayout/CommitmentRow/CommitmentValueLabel");
        _commitmentConfirmButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Family/FamilyScroll/FamilyLayout/CommitmentCard/CommitmentCardLayout/CommitmentConfirmButton");
        _commitmentSlider.ValueChanged += _ => _commitmentValueLabel.Text = string.Format(CommitmentValueFormat, (int)_commitmentSlider.Value);
        _commitmentConfirmButton.Pressed += OnCommitmentConfirmPressed;

        _saveButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Settings/SettingsScroll/SettingsLayout/SaveCard/SaveCardLayout/SaveButton");
        _saveStatusLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Settings/SettingsScroll/SettingsLayout/SaveCard/SaveCardLayout/SaveStatusLabel");
        _saveButton.Pressed += OnSavePressed;

        _quitButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Settings/SettingsScroll/SettingsLayout/QuitCard/QuitCardLayout/QuitButton");
        _quitConfirmDialog = GetNode<ConfirmationDialog>("Screen/ScreenLayout/PhoneTabs/Settings/SettingsScroll/SettingsLayout/QuitCard/QuitCardLayout/QuitConfirmDialog");
        _quitButton.Pressed += OnQuitPressed;
        _quitConfirmDialog.Confirmed += OnQuitConfirmed;

        _replayButton = GetNode<Button>("Screen/ScreenLayout/PhoneTabs/Settings/SettingsScroll/SettingsLayout/ReplayCard/ReplayCardLayout/ReplayButton");
        _replayButton.Pressed += OnReplayPressed;

        _volumeSlider = GetNode<HSlider>("Screen/ScreenLayout/PhoneTabs/Settings/SettingsScroll/SettingsLayout/OptionsCard/OptionsCardLayout/VolumeRow/VolumeSlider");
        _volumeSlider.SetValueNoSignal(UiSfx.Instance.Volume);
        _volumeSlider.ValueChanged += OnVolumeChanged;
        _volumeSlider.DragEnded += OnVolumeDragEnded;
    }

    public override void _ExitTree()
    {
        _contactList.ItemSelected -= OnContactSelected;
        _loadOlderButton.Pressed -= OnLoadOlderPressed;
        _narcoticsButton.Pressed -= OnNarcoticsPressed;
        _fencingButton.Pressed -= OnFencingPressed;
        _pokerButton.Pressed -= OnPokerPressed;
        _proShopButton.Pressed -= OnProShopPressed;
        _buyMinutesButton.Pressed -= OnBuyMinutesPressed;
        _upgradePhoneButton.Pressed -= OnUpgradePhonePressed;
        _phoneTabs.TabChanged -= OnPhoneTabChanged;
        _commitmentConfirmButton.Pressed -= OnCommitmentConfirmPressed;
        _saveButton.Pressed -= OnSavePressed;
        _quitButton.Pressed -= OnQuitPressed;
        _quitConfirmDialog.Confirmed -= OnQuitConfirmed;
        _replayButton.Pressed -= OnReplayPressed;
        _volumeSlider.ValueChanged -= OnVolumeChanged;
        _volumeSlider.DragEnded -= OnVolumeDragEnded;
    }

    private static void OnVolumeChanged(double value)
    {
        UiSfx.Instance.SetVolume((float)value);
    }

    private static void OnVolumeDragEnded(bool valueChanged)
    {
        UiSfx.Instance.SaveVolume();
        if (valueChanged)
        {
            // Volume preview: hear the chosen level at the moment it's set.
            UiSfx.Instance.Play(UiSound.Tap);
        }
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

        RefreshClock(gm);
        RefreshPhoneSnapshot(gm);
        RefreshBankTab(gm);
        RefreshMarketplaceTab(gm);
        RefreshFamilyTab(gm);

        bool hasPending = gm.GrittyEventChoices.TryGetPendingChoice(out PendingGrittyChoice pending);
        bool unchanged = _initialized
            && gm.State.CurrentDay == _shownDay
            && hasPending == _shownHasPending
            && (!hasPending
                || (pending.Fired.EventId == _shownEventId
                    && pending.Fired.SubjectPlayerId == _shownSubjectId
                    && pending.Fired.Day == _shownFireDay));
        if (unchanged)
        {
            return;
        }
        _initialized = true;
        _shownDay = gm.State.CurrentDay;
        _shownHasPending = hasPending;
        _shownEventId = hasPending ? pending.Fired.EventId : null;
        _shownSubjectId = hasPending ? pending.Fired.SubjectPlayerId : null;
        _shownFireDay = hasPending ? pending.Fired.Day : -1;

        ReloadMessages(gm);
        RenderEventsFeed(gm, hasPending ? pending : null);

        RefreshContactList(gm);
        if (_activeContactId is not null)
        {
            RenderThread(gm, _activeContactId);
        }
    }

    /// <summary>
    /// The status-bar clock: the human calendar date plus the live
    /// time-of-day ("TUE APR 14 · 2:47 PM") — Slice G-3, a read-only view of
    /// the same GameClock the TimeControlBar drives, so the phone ticks in
    /// lockstep with the bar. Formats only when the day or displayed minute
    /// actually changes (ui_conventions.md: no per-frame formatting).
    /// </summary>
    private void RefreshClock(GameManager gm)
    {
        long currentDay = gm.State.CurrentDay;
        int minute = gm.TimeOfDay.MinuteOfDay;
        if (currentDay == _shownClockDay && minute == _shownClockMinute)
        {
            return;
        }
        _shownClockDay = currentDay;
        _shownClockMinute = minute;
        int dos = GlobalState.DayOfSeasonForDay(currentDay);
        CalendarDate date = GameCalendar.DateForDayOfSeason(dos);
        Weekday weekday = GameCalendar.WeekdayForDayOfSeason(dos);
        int hour = minute / 60;
        int hour12 = hour % 12 == 0 ? 12 : hour % 12;
        _clockLabel.Text = string.Format(
            ClockFormat,
            GameCalendar.NameOf(weekday).Substring(0, 3).ToUpperInvariant(),
            GameCalendar.MonthName(date.Month).Substring(0, 3).ToUpperInvariant(),
            date.Day,
            hour12,
            minute % 60,
            hour < 12 ? ClockAmText : ClockPmText);
    }

    private void OnContactSelected(long index)
    {
        if (index < 0 || index >= _orderedContactIds.Count)
        {
            return;
        }
        GameManager gm = GameManager.Instance!;
        string contactId = _orderedContactIds[(int)index];

        MarkRead(contactId);
        RenderThread(gm, contactId);
        // Clearing the unread marker on the item just opened needs the list
        // relabeled; cheap at this scale (a handful of contacts).
        RefreshContactList(gm);
    }

    /// <summary>Splits the read-back into the Events feed (Event-kind, one flat chronological list) and the Messages tab's per-contact Text-kind threads.</summary>
    private void ReloadMessages(GameManager gm)
    {
        gm.NarrativeLog.LoadForPlayer(gm.Career.AvatarPlayerId, gm.State.CurrentDay, _loadScratch);
        _eventRows.Clear();
        _messagesByContact.Clear();
        foreach (NarrativeMessageRow row in _loadScratch)
        {
            if (row.Kind == NarrativeMessageKind.Event)
            {
                _eventRows.Add(row);
                continue;
            }
            if (!_messagesByContact.TryGetValue(row.ContactId, out List<NarrativeMessageRow>? thread))
            {
                thread = new List<NarrativeMessageRow>();
                _messagesByContact[row.ContactId] = thread;
            }
            thread.Add(row);
        }
        if (_eventRows.Count > MaxEventCards)
        {
            _eventRows.RemoveRange(0, _eventRows.Count - MaxEventCards);
        }
    }

    /// <summary>The Messages tab's contact list: most-recent-text-first, unread marker. Pending-choice sort-to-top no longer applies here — that lives on the Events tab now.</summary>
    private void RefreshContactList(GameManager gm)
    {
        _orderedContactIds.Clear();
        _orderedContactIds.AddRange(_messagesByContact.Keys);
        _orderedContactIds.Sort((a, b) => LastDayFor(b).CompareTo(LastDayFor(a)));

        int selectedIndex = -1;
        _contactList.Clear();
        for (int i = 0; i < _orderedContactIds.Count; i++)
        {
            string contactId = _orderedContactIds[i];
            ContactDefinition contact = gm.Contacts.Resolve(contactId);
            bool unread = MessageCount(contactId) > _lastSeenCount.GetValueOrDefault(contactId);
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

    /// <summary>
    /// The Events tab: every past Event row as a resolved card, oldest first,
    /// then — when the avatar has a pending choice — one more unresolved card
    /// with the reply-chip buttons underneath (relocated from the old
    /// Messages thread panel; §4.3 never-gates invariant unchanged, this tab
    /// carries no minute cost or tier lock, same as Messages).
    /// </summary>
    private void RenderEventsFeed(GameManager gm, PendingGrittyChoice? pending)
    {
        foreach (Node child in _eventsContainer.GetChildren())
        {
            child.QueueFree();
        }
        foreach (Node child in _choicesContainer.GetChildren())
        {
            child.QueueFree();
        }

        foreach (NarrativeMessageRow row in _eventRows)
        {
            string resolutionLine = string.IsNullOrEmpty(row.Outcome)
                ? string.Format(OutcomeFallbackFormat, row.Choice)
                : row.Outcome;
            _eventsContainer.AddChild(BuildEventCard(EventCategoryCodec.FromWire(row.Category), row.Prompt, resolutionLine, row.GameDay, row.SeasonYear));
        }

        if (pending is { } liveFire)
        {
            GrittyEventFiredEvent fired = liveFire.Fired;
            _eventsContainer.AddChild(BuildEventCard(liveFire.Definition.Category, liveFire.Definition.Prompt, null,
                fired.Day, gm.State.SeasonYearForDay(fired.Day)));

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

        ScrollEventsToNewest();
    }

    /// <summary>
    /// Snaps the Events feed to its newest card (the pending choice, when one
    /// is live) after a re-render. The final scroll range only exists once
    /// the QueueFree'd old cards are gone and the container has re-sorted —
    /// both land over the next frames — so the snap waits two process frames
    /// before reading it.
    /// </summary>
    private async void ScrollEventsToNewest()
    {
        SceneTree tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }
        _eventsScroll.ScrollVertical = (int)_eventsScroll.GetVScrollBar().MaxValue;
    }

    /// <summary>One Events/History-feed card: category + day/season caption, the scene prompt, and (when resolved) the resolution line. <paramref name="resolutionLine"/> is null for the live unresolved card. Returns the built card unparented — callers add it to whichever container owns it.</summary>
    private PanelContainer BuildEventCard(EventCategory category, string prompt, string? resolutionLine, long gameDay, int seasonYear)
    {
        int dayOfSeason = GlobalState.DayOfSeasonForDay(gameDay);

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 4);

        var metaRow = new HBoxContainer();
        metaRow.AddChild(new Label
        {
            Text = CategoryLabels[(int)category],
            ThemeTypeVariation = "HeadingLabel",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        metaRow.AddChild(new Label
        {
            Text = string.Format(TimestampFormat, seasonYear, dayOfSeason),
            ThemeTypeVariation = "CaptionLabel",
        });
        layout.AddChild(metaRow);

        layout.AddChild(new Label { Text = prompt, AutowrapMode = TextServer.AutowrapMode.WordSmart });

        if (!string.IsNullOrEmpty(resolutionLine))
        {
            layout.AddChild(new Label
            {
                Text = resolutionLine,
                ThemeTypeVariation = "CaptionLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        card.AddChild(layout);
        return card;
    }

    /// <summary>
    /// The History tab: a read-only archive over Event-kind rows the live
    /// Events feed no longer keeps once they age past <see cref="MaxEventCards"/>.
    /// Unlike the Events/Messages tabs it never dirty-flag-refreshes in
    /// <see cref="_Process"/> — it only grows when the player presses "Load
    /// Older" (or on the tab's first visit, via <see cref="OnPhoneTabChanged"/>),
    /// each click paging one <see cref="HistoryPageSize"/> batch further back
    /// via <see cref="NarrativeLogQueries.LoadEventPageBefore"/> and prepending
    /// it above whatever was already loaded.
    /// </summary>
    private void LoadOlderHistory(GameManager gm)
    {
        if (_historyReachedBeginning)
        {
            return;
        }

        gm.NarrativeLog.LoadEventPageBefore(
            gm.Career.AvatarPlayerId, gm.State.CurrentDay, _historyCursor, HistoryPageSize,
            _historyPageScratch, out bool reachedBeginning);
        _historyReachedBeginning = reachedBeginning;

        if (_historyPageScratch.Count > 0)
        {
            _historyCursor = _historyPageScratch[0].LogId; // oldest-first within the page; the next call continues strictly before this
            int insertIndex = 0;
            foreach (NarrativeMessageRow row in _historyPageScratch)
            {
                string resolutionLine = string.IsNullOrEmpty(row.Outcome)
                    ? string.Format(OutcomeFallbackFormat, row.Choice)
                    : row.Outcome;
                Control card = BuildEventCard(EventCategoryCodec.FromWire(row.Category), row.Prompt, resolutionLine, row.GameDay, row.SeasonYear);
                _historyContainer.AddChild(card);
                _historyContainer.MoveChild(card, insertIndex);
                insertIndex++;
            }
        }

        _loadOlderButton.Visible = !_historyReachedBeginning;
        _historyStatusLabel.Visible = _historyReachedBeginning;
        if (_historyReachedBeginning)
        {
            _historyStatusLabel.Text = HistoryBeginningText;
        }
        ScrollHistoryToTop();
    }

    private void OnLoadOlderPressed()
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }
        LoadOlderHistory(gm);
    }

    /// <summary>
    /// Snaps to the top of the scroll view after a "Load Older" prepend —
    /// since new content always lands above whatever was already loaded,
    /// scrolling to 0 always lands exactly on the top of the just-inserted
    /// page. Same two-frame wait as <see cref="ScrollEventsToNewest"/> (the
    /// new range only exists once the container has re-laid-out).
    /// </summary>
    private async void ScrollHistoryToTop()
    {
        SceneTree tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }
        _historyScroll.ScrollVertical = 0;
    }

    /// <summary>The Messages tab's selected thread: Text-kind rows only, one incoming bubble per companion text (no player-reply bubble — a text has no in-place answer, unlike the old embedded event choices).</summary>
    private void RenderThread(GameManager gm, string contactId)
    {
        _activeContactId = contactId;
        ContactDefinition contact = gm.Contacts.Resolve(contactId);
        _threadHeaderLabel.Text = contact.DisplayName;
        _threadPortrait.SetIdentity(contact.PortraitKey, contact.DisplayName);

        foreach (Node child in _threadContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (_messagesByContact.TryGetValue(contactId, out List<NarrativeMessageRow>? thread))
        {
            foreach (NarrativeMessageRow row in thread)
            {
                int dayOfSeason = GlobalState.DayOfSeasonForDay(row.GameDay);
                AddBubble(row.Body, incoming: true, string.Format(TimestampFormat, row.SeasonYear, dayOfSeason));
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

    private int MessageCount(string contactId) =>
        _messagesByContact.TryGetValue(contactId, out List<NarrativeMessageRow>? thread) ? thread.Count : 0;

    private void MarkRead(string contactId) => _lastSeenCount[contactId] = MessageCount(contactId);

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
            RefreshCriticalNeeds(needs);
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
    /// and answering event threads never costs a minute. The History tab is
    /// likewise free (§4.3 posture) — its own branch below just triggers the
    /// first "Load Older" page on the tab's first visit, same no-minute-cost
    /// footing as Events/Messages.
    /// </summary>
    private void OnPhoneTabChanged(long tabIndex)
    {
        if ((int)tabIndex == _historyTabIndex)
        {
            GameManager gm = GameManager.Instance!;
            if (gm.Career.HasAvatar && _historyContainer.GetChildCount() == 0)
            {
                LoadOlderHistory(gm);
            }
            return;
        }
        if ((int)tabIndex != _marketplaceTabIndex || !_hasPhoneRow)
        {
            return;
        }
        GameManager marketplaceGm = GameManager.Instance!;
        if (!marketplaceGm.Career.HasAvatar)
        {
            return;
        }
        if (_phoneRow.Plan == PhoneService.UnlimitedPlan || _homeWifi)
        {
            return; // free browse, nothing written
        }
        marketplaceGm.Phone.TrySpendMinutes(
            marketplaceGm.Career.AvatarPlayerId, PhoneService.MarketplaceBrowseMinuteCost, onWifi: false, out _);
        _phoneLoadedDay = long.MinValue; // re-snapshot next frame
    }

    private static bool NeedsEqual(in NeedsState a, in NeedsState b) =>
        a.Hunger == b.Hunger && a.Sleep == b.Sleep && a.Hygiene == b.Hygiene
        && a.Social == b.Social && a.Fitness == b.Fitness;

    private static void SetCriticalMarkAnchor(ColorRect mark, float anchor)
    {
        mark.AnchorLeft = anchor;
        mark.AnchorRight = anchor;
    }

    /// <summary>
    /// Onboarding-overlay doc §4.1: the moment a need is at/under
    /// <see cref="NeedsEngine.CriticalThreshold"/> the sim overrides the
    /// player's plan (LifeSimManager's crisis branch) — this makes that state
    /// loud: the row label goes danger-red and the caption under the card
    /// names the need(s). Mask-dirty-flagged so the recolor and the caption
    /// string rebuild happen only when a need crosses the line.
    /// </summary>
    private void RefreshCriticalNeeds(in NeedsState needs)
    {
        int mask = (needs.Hunger <= NeedsEngine.CriticalThreshold ? 1 : 0)
            | (needs.Sleep <= NeedsEngine.CriticalThreshold ? 2 : 0)
            | (needs.Hygiene <= NeedsEngine.CriticalThreshold ? 4 : 0)
            | (needs.Social <= NeedsEngine.CriticalThreshold ? 8 : 0)
            | (needs.Fitness <= NeedsEngine.CriticalThreshold ? 16 : 0);
        if (mask == _shownCriticalMask)
        {
            return;
        }
        _shownCriticalMask = mask;

        ApplyCriticalColor(_hungerLabel, (mask & 1) != 0);
        ApplyCriticalColor(_sleepLabel, (mask & 2) != 0);
        ApplyCriticalColor(_hygieneLabel, (mask & 4) != 0);
        ApplyCriticalColor(_socialLabel, (mask & 8) != 0);
        ApplyCriticalColor(_fitnessLabel, (mask & 16) != 0);

        _needsCriticalCaptionLabel.Visible = mask != 0;
        if (mask == 0)
        {
            return;
        }

        // The row labels' text IS each need's display name — reused here so
        // the caption can never disagree with the rows.
        _criticalNamesScratch.Clear();
        if ((mask & 1) != 0) _criticalNamesScratch.Add(_hungerLabel.Text);
        if ((mask & 2) != 0) _criticalNamesScratch.Add(_sleepLabel.Text);
        if ((mask & 4) != 0) _criticalNamesScratch.Add(_hygieneLabel.Text);
        if ((mask & 8) != 0) _criticalNamesScratch.Add(_socialLabel.Text);
        if ((mask & 16) != 0) _criticalNamesScratch.Add(_fitnessLabel.Text);
        string names = _criticalNamesScratch.Count == 1
            ? _criticalNamesScratch[0]
            : string.Join(", ", _criticalNamesScratch.GetRange(0, _criticalNamesScratch.Count - 1))
                + " and " + _criticalNamesScratch[^1];
        _needsCriticalCaptionLabel.Text = string.Format(
            _criticalNamesScratch.Count == 1 ? NeedCriticalFormat : NeedsCriticalPluralFormat, names);
    }

    private static void ApplyCriticalColor(Label label, bool critical)
    {
        if (critical)
        {
            label.AddThemeColorOverride("font_color", UiColors.Danger);
        }
        else
        {
            label.RemoveThemeColorOverride("font_color");
        }
    }

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

    /// <summary>
    /// HS-5 §7.1: the Family tab — the avatar's children (Care/Coaching/
    /// Funding/Neglect, rebuilt fresh each refresh since the roster only
    /// ever grows by one at a time and this runs once per day change, never
    /// per-frame) plus the standing weekly funding commitment. No
    /// Child_Development row means the pure-nature neutral default
    /// (ChildDevelopmentRow.Neutral), same "absent row = neutral" contract
    /// TryGetChild's every other caller relies on.
    /// </summary>
    private void RefreshFamilyTab(GameManager gm)
    {
        if (gm.State.CurrentDay == _familyLoadedDay)
        {
            return;
        }
        _familyLoadedDay = gm.State.CurrentDay;
        string avatarId = gm.Career.AvatarPlayerId;

        int weeklyFunding = gm.Persons.TryGetChildRearingCommitment(avatarId, out ChildRearingCommitmentRow commitment)
            ? commitment.WeeklyFunding
            : 0;
        _commitmentSlider.SetValueNoSignal(weeklyFunding);
        _commitmentValueLabel.Text = string.Format(CommitmentValueFormat, weeklyFunding);

        foreach (Node existing in _childrenContainer.GetChildren())
        {
            existing.QueueFree();
        }
        int childCount = gm.Players.LoadChildrenOf(avatarId, _familyChildrenScratch);
        _noChildrenLabel.Visible = childCount == 0;
        _childrenContainer.Visible = childCount > 0;
        foreach (PlayerRow child in _familyChildrenScratch)
        {
            ChildDevelopmentRow axes = gm.Persons.TryGetChild(child.PlayerId, out ChildDevelopmentRow row)
                ? row
                : ChildDevelopmentRow.Neutral(child.PlayerId);
            _childrenContainer.AddChild(BuildChildCard(in child, in axes));
        }
    }

    private Control BuildChildCard(in PlayerRow child, in ChildDevelopmentRow axes)
    {
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 4);
        layout.AddChild(new Label
        {
            Text = string.Format(ChildCardHeadingFormat, child.FirstName, child.Age),
            ThemeTypeVariation = "HeadingLabel",
        });
        layout.AddChild(BuildAxisRow(CareLabelText, axes.Care));
        layout.AddChild(BuildAxisRow(CoachingLabelText, axes.Coaching));
        layout.AddChild(BuildAxisRow(FundingLabelText, axes.Funding));
        layout.AddChild(BuildAxisRow(NeglectLabelText, axes.Neglect));

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        card.AddChild(layout);
        return card;
    }

    private static Control BuildAxisRow(string label, int value)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(70, 0) });
        row.AddChild(new ProgressBar
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            Value = value,
        });
        return row;
    }

    private void OnCommitmentConfirmPressed()
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }
        gm.SetWeeklyChildFunding(gm.Career.AvatarPlayerId, (int)_commitmentSlider.Value);
    }

    /// <summary>
    /// Settings tab: the Save Now intent — <see cref="GameManager.SaveNow"/>
    /// flushes the live mirrors and folds the WAL into the single .db.
    /// Persistence is already continuous (every mutation commits), so this is
    /// player reassurance plus the mid-day accumulator flush.
    /// </summary>
    private void OnSavePressed()
    {
        GameManager gm = GameManager.Instance!;
        bool saved = gm.SaveNow();
        _saveStatusLabel.Visible = true;
        _saveStatusLabel.Text = saved
            ? string.Format(SavedStatusFormat, gm.State.CurrentDay)
            : SaveFailedText;
    }

    private void OnQuitPressed() => _quitConfirmDialog.PopupCentered();

    /// <summary>Flushes the same save path as "Save Now" before handing off to the engine — belt-and-suspenders on top of the already-continuous autosave.</summary>
    private void OnQuitConfirmed()
    {
        GameManager.Instance!.SaveNow();
        GetTree().Quit();
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

    private void OnReplayPressed() => EmitSignal(SignalName.TutorialReplayRequested);
}
