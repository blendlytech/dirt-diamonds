using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Narrative.Contacts;
using DirtAndDiamonds.Narrative.Events;
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
    [Export]
    public string TimestampFormat { get; set; } = "Season {0}, day {1}";

    [Export]
    public string UnreadMarker { get; set; } = "● ";

    [Export]
    public string NoContactSelectedText { get; set; } = "Select a contact";

    private ItemList _contactList = null!;
    private Label _threadHeaderLabel = null!;
    private VBoxContainer _threadContainer = null!;
    private VBoxContainer _choicesContainer = null!;

    private readonly Dictionary<string, List<NarrativeMessageRow>> _messagesByContact = new();
    private readonly Dictionary<string, int> _lastSeenCount = new();
    private readonly List<string> _orderedContactIds = new();
    private readonly List<NarrativeMessageRow> _loadScratch = new();

    private string? _activeContactId;
    private string? _shownFireIdentity;
    private bool _initialized;

    public override void _Ready()
    {
        _contactList = GetNode<ItemList>("PhoneTabs/Messages/MessagesLayout/ContactList");
        _threadHeaderLabel = GetNode<Label>("PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadHeaderLabel");
        _threadContainer = GetNode<VBoxContainer>("PhoneTabs/Messages/MessagesLayout/ThreadPanel/ThreadScroll/ThreadContainer");
        _choicesContainer = GetNode<VBoxContainer>("PhoneTabs/Messages/MessagesLayout/ThreadPanel/ChoicesContainer");
        _contactList.ItemSelected += OnContactSelected;
        _threadHeaderLabel.Text = NoContactSelectedText;
    }

    public override void _ExitTree()
    {
        _contactList.ItemSelected -= OnContactSelected;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            return;
        }

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
}
