using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Narrative.Pipeline;

/// <summary>
/// The pipeline's phone texts (slice 13-6, brief D5-A): a Narrative-layer
/// subscriber that turns the pipeline's Core events into the avatar's texts.
/// It never references Baseball — it learns about the pipeline only from
/// <see cref="RecruitingOffersEvent"/>, <see cref="RecruitSignedEvent"/>,
/// <see cref="DraftDeclarationOpenedEvent"/>, <see cref="DraftCompletedEvent"/>
/// and <see cref="AvatarReleasedEvent"/> (primitives) — and its writes commit
/// in their own batch, never a Baseball one.
///
/// <list type="bullet">
/// <item>Offers (<c>coach_hs</c>): the offers, or the walk-on.</item>
/// <item>Signing Day (<c>coach_college</c>): welcome to the program.</item>
/// <item>The declaration (<c>agent_diaz</c>): projected in a round, projected
/// undrafted, or (a senior) automatically in the pool.</item>
/// <item>Draft Night (<c>agent_diaz</c>): drafted, or (a senior) undrafted.</item>
/// <item>The release (<c>coach_college</c> or <c>coach_reyes</c>): a
/// parent-phase or a game-over variant.</item>
/// </list>
///
/// Copy lives in the strings table (<c>pipeline.*</c> keys, placeholder lines
/// Claude Sonnet 5.5 polishes later; no <c>{tokens}</c>). The father's first
/// text is Phase L's, keyed on <see cref="ProContractSignedEvent"/>.
/// </summary>
public sealed class PipelineBeatWriter
{
    public const string OffersKey = "pipeline.offers.received";
    public const string WalkOnKey = "pipeline.offers.walk_on";
    public const string SignedKey = "pipeline.signing.signed";
    public const string DeclareProjectedKey = "pipeline.declare.projected";
    public const string DeclareUndraftedKey = "pipeline.declare.off_board";
    public const string DeclareAutomaticKey = "pipeline.declare.automatic";
    public const string DraftedKey = "pipeline.draft.picked";
    public const string UndraftedKey = "pipeline.draft.undrafted";
    public const string ReleaseCollegeParentKey = "pipeline.release.college.parent_phase";
    public const string ReleaseCollegeGameOverKey = "pipeline.release.college.game_over";
    public const string ReleaseProParentKey = "pipeline.release.pro.parent_phase";
    public const string ReleaseProGameOverKey = "pipeline.release.pro.game_over";

    public const string HsCoachContact = "coach_hs";
    public const string CollegeCoachContact = "coach_college";
    public const string ProCoachContact = "coach_reyes";
    public const string AgentContact = "agent_diaz";

    /// <summary>The LeagueTier value of College (Narrative can't reference the Baseball enum's tier rules).</summary>
    private const byte CollegeTier = 1;

    private readonly DatabaseManager _db;
    private readonly NarrativeLogQueries _log;
    private readonly GlobalState _state;
    private readonly Action<RecruitingOffersEvent> _onOffers;
    private readonly Action<RecruitSignedEvent> _onSigned;
    private readonly Action<DraftDeclarationOpenedEvent> _onDeclarationOpened;
    private readonly Action<DraftCompletedEvent> _onDraftCompleted;
    private readonly Action<AvatarReleasedEvent> _onReleased;

    /// <summary>Resolves a strings-table key to its text at write time; null writes the key.</summary>
    public Func<string, string>? ResolveText;

    /// <summary>Texts written since construction (harness visibility).</summary>
    public int TextsWritten { get; private set; }

    /// <summary>The last text's key and contact (harness visibility).</summary>
    public string LastKey { get; private set; } = "";
    public string LastContact { get; private set; } = "";

    public PipelineBeatWriter(DatabaseManager db, NarrativeLogQueries log, GlobalState state)
    {
        _db = db;
        _log = log;
        _state = state;
        _onOffers = OnOffers;
        _onSigned = OnSigned;
        _onDeclarationOpened = OnDeclarationOpened;
        _onDraftCompleted = OnDraftCompleted;
        _onReleased = OnReleased;
    }

    public void AttachTo(EventBus bus)
    {
        bus.Subscribe(_onOffers);
        bus.Subscribe(_onSigned);
        bus.Subscribe(_onDeclarationOpened);
        bus.Subscribe(_onDraftCompleted);
        bus.Subscribe(_onReleased);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onOffers);
        bus.Unsubscribe(_onSigned);
        bus.Unsubscribe(_onDeclarationOpened);
        bus.Unsubscribe(_onDraftCompleted);
        bus.Unsubscribe(_onReleased);
    }

    private void OnOffers(RecruitingOffersEvent e) =>
        WriteText(e.PlayerId, HsCoachContact, e.IsWalkOn ? WalkOnKey : OffersKey);

    private void OnSigned(RecruitSignedEvent e) => WriteText(e.PlayerId, CollegeCoachContact, SignedKey);

    private void OnDeclarationOpened(DraftDeclarationOpenedEvent e)
    {
        string key = e.IsAutomatic ? DeclareAutomaticKey : e.ProjectedRound > 0 ? DeclareProjectedKey : DeclareUndraftedKey;
        WriteText(e.PlayerId, AgentContact, key);
    }

    // Stateless (13-6a S1): everything Draft Night needs rides on the event,
    // so a save and reload between day 176 and day 195 still gets its text.
    private void OnDraftCompleted(DraftCompletedEvent e)
    {
        if (e.AvatarPlayerId is not string avatarId)
        {
            return; // the avatar wasn't draft-eligible this season
        }
        if (e.AvatarOverallPick > 0)
        {
            WriteText(avatarId, AgentContact, DraftedKey);
        }
        else if (e.AvatarWasAutomaticSenior)
        {
            // Only a senior is certainly in the pool; an undrafted underclassman simply returns.
            WriteText(avatarId, AgentContact, UndraftedKey);
        }
    }

    private void OnReleased(AvatarReleasedEvent e)
    {
        bool college = e.Tier == CollegeTier;
        string key = college
            ? (e.EnteredParentPhase ? ReleaseCollegeParentKey : ReleaseCollegeGameOverKey)
            : (e.EnteredParentPhase ? ReleaseProParentKey : ReleaseProGameOverKey);
        WriteText(e.PlayerId, college ? CollegeCoachContact : ProCoachContact, key);
    }

    private void WriteText(string playerId, string contactId, string key)
    {
        string body = ResolveText?.Invoke(key) ?? key;
        _db.BeginBatch();
        try
        {
            _log.InsertText(_state.SeasonYear, _state.CurrentDay, playerId, contactId, body);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        TextsWritten++;
        LastKey = key;
        LastContact = contactId;
    }
}
