using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// New-game avatar creation: name, team, a rolled/re-rollable HS-2 backstory
/// reveal (family wealth tier, parents, phone, transport, strictness), a
/// personality-trait picker (offsets the person-stat seed + writes trait_*
/// Entity_Flags post-create), batter-or-pitcher career (with a bullpen role
/// sub-choice for pitchers), and a fixed-budget ratings allocator over the
/// three career-specific attributes plus fielding. Calls straight into
/// <see cref="DirtAndDiamonds.Simulation.Baseball.CareerManager.CreateAvatar"/>
/// on submit, which owns every side effect (teammate benching, household
/// seeding, arsenal generation, sim re-init) — this screen only collects and
/// validates input. UI never touches the database directly, per
/// ui_conventions. Node paths verified against NewGameScreen.tscn via
/// godot_scene_mapper before this script was written.
/// </summary>
public sealed partial class NewGameScreen : Control
{
    [Signal]
    public delegate void AvatarCreatedEventHandler();

    [Export]
    public string RemainingPointsFormat { get; set; } = "Points remaining: {0}";

    [Export]
    public string NameRequiredText { get; set; } = "Enter a name before starting your career.";

    [Export]
    public string WealthTierFormat { get; set; } = "Family: {0}";

    [Export]
    public string WealthTierNamesCsv { get; set; } = "Destitute,Working-Class,Middle-Class,Comfortable,Wealthy";

    [Export]
    public string HouseholdIncomeFormat { get; set; } = "Household income: ~${0:N0}/yr";

    [Export]
    public string StartingFundsFormat { get; set; } = "Starting funds: ${0:N0}";

    [Export]
    public string AllowanceFormat { get; set; } = "Weekly allowance: ${0:N0}";

    [Export]
    public string PhoneFormat { get; set; } = "Phone: {0}, {1} plan";

    [Export]
    public string PhoneTierNamesCsv { get; set; } = "Burner Phone,Mid-Tier Phone,Flagship Phone";

    [Export]
    public string PhonePlanNamesCsv { get; set; } = "Prepaid,Basic,Unlimited";

    [Export]
    public string WifiYesText { get; set; } = "Home Wi-Fi: yes";

    [Export]
    public string WifiNoText { get; set; } = "Home Wi-Fi: no — you'll have to find it elsewhere";

    [Export]
    public string TransportFormat { get; set; } = "Transport: {0}";

    [Export]
    public string TransportNoneText { get; set; } = "none (you're on foot for now)";

    [Export]
    public string StrictnessFormat { get; set; } = "Parental strictness: {0}/100";

    [Export]
    public string ParentsFormat { get; set; } = "Parents: {0} ({1}) & {2} ({3})";

    [Export]
    public string TraitsHintFormat { get; set; } = "Choose up to {0} traits ({1} selected)";

    // Every rookie starts at league-average (50) on all seven ratings; the
    // budget below equals the four relevant sliders' combined baseline
    // (4 * 50), so allocation starts valid and every reassignment is a
    // trade-off rather than a free bonus.
    private const int MinRating = 20;
    private const int MaxRating = 90;
    private const int RatingBudget = 200;

    // Personality picks nudge one person stat each (first-pass tunable
    // magnitude — the person layer doc's §2.1 consumer table, not a rolled
    // spread) and always write a matching trait_* Entity_Flag so future
    // Gritty Event content can gate on the choice directly.
    private const int MaxTraitPicks = 2;
    private const int TraitStatOffset = 8;

    private readonly record struct TraitDef(string FlagName, string StatName);

    private static readonly TraitDef[] TraitDefs =
    {
        new("trait_leader", "Teamwork"),
        new("trait_funny", "Charisma"),
        new("trait_generous", "Morality"),
        new("trait_kind", "Reputation"),
        new("trait_patient", "Discipline"),
        new("trait_humble", "Maturity"),
    };

    private LineEdit _nameLineEdit = null!;
    private OptionButton _teamOptionButton = null!;
    private Label _wealthTierLabel = null!;
    private Label _householdIncomeLabel = null!;
    private Label _startingFundsLabel = null!;
    private Label _allowanceLabel = null!;
    private Label _phoneLabel = null!;
    private Label _wifiLabel = null!;
    private Label _transportLabel = null!;
    private Label _strictnessLabel = null!;
    private Label _parentsLabel = null!;
    private Button _rerollButton = null!;
    private Label _traitsHintLabel = null!;
    private CheckBox[] _traitCheckBoxes = null!;
    private Button _batterButton = null!;
    private Button _pitcherButton = null!;
    private Control _roleRow = null!;
    private OptionButton _roleOptionButton = null!;
    private Control _batterRatings = null!;
    private HSlider _powerSlider = null!;
    private Label _powerValueLabel = null!;
    private HSlider _contactSlider = null!;
    private Label _contactValueLabel = null!;
    private HSlider _disciplineSlider = null!;
    private Label _disciplineValueLabel = null!;
    private Control _pitcherRatings = null!;
    private HSlider _stuffSlider = null!;
    private Label _stuffValueLabel = null!;
    private HSlider _controlSlider = null!;
    private Label _controlValueLabel = null!;
    private HSlider _staminaSlider = null!;
    private Label _staminaValueLabel = null!;
    private HSlider _fieldingSlider = null!;
    private Label _fieldingValueLabel = null!;
    private Label _remainingPointsLabel = null!;
    private Label _errorLabel = null!;
    private Button _createButton = null!;

    private RngState _revealRng;
    private Backstory _backstory;

    public override void _Ready()
    {
        _nameLineEdit = GetNode<LineEdit>("Screen/NameRow/NameLineEdit");
        _teamOptionButton = GetNode<OptionButton>("Screen/TeamRow/TeamOptionButton");
        _wealthTierLabel = GetNode<Label>("Screen/BackgroundRow/WealthTierLabel");
        _householdIncomeLabel = GetNode<Label>("Screen/BackgroundRow/HouseholdIncomeLabel");
        _startingFundsLabel = GetNode<Label>("Screen/BackgroundRow/StartingFundsLabel");
        _allowanceLabel = GetNode<Label>("Screen/BackgroundRow/AllowanceLabel");
        _phoneLabel = GetNode<Label>("Screen/BackgroundRow/PhoneLabel");
        _wifiLabel = GetNode<Label>("Screen/BackgroundRow/WifiLabel");
        _transportLabel = GetNode<Label>("Screen/BackgroundRow/TransportLabel");
        _strictnessLabel = GetNode<Label>("Screen/BackgroundRow/StrictnessLabel");
        _parentsLabel = GetNode<Label>("Screen/BackgroundRow/ParentsLabel");
        _rerollButton = GetNode<Button>("Screen/BackgroundRow/RerollButton");
        _traitsHintLabel = GetNode<Label>("Screen/TraitsRow/TraitsHintLabel");
        _traitCheckBoxes = new[]
        {
            GetNode<CheckBox>("Screen/TraitsRow/TraitsGrid/LeadershipCheck"),
            GetNode<CheckBox>("Screen/TraitsRow/TraitsGrid/HumorCheck"),
            GetNode<CheckBox>("Screen/TraitsRow/TraitsGrid/GenerosityCheck"),
            GetNode<CheckBox>("Screen/TraitsRow/TraitsGrid/KindnessCheck"),
            GetNode<CheckBox>("Screen/TraitsRow/TraitsGrid/PatienceCheck"),
            GetNode<CheckBox>("Screen/TraitsRow/TraitsGrid/HumilityCheck"),
        };
        _batterButton = GetNode<Button>("Screen/CareerRow/BatterButton");
        _pitcherButton = GetNode<Button>("Screen/CareerRow/PitcherButton");
        _roleRow = GetNode<Control>("Screen/RoleRow");
        _roleOptionButton = GetNode<OptionButton>("Screen/RoleRow/RoleOptionButton");
        _batterRatings = GetNode<Control>("Screen/BatterRatings");
        _powerSlider = GetNode<HSlider>("Screen/BatterRatings/PowerRow/PowerSlider");
        _powerValueLabel = GetNode<Label>("Screen/BatterRatings/PowerRow/PowerValueLabel");
        _contactSlider = GetNode<HSlider>("Screen/BatterRatings/ContactRow/ContactSlider");
        _contactValueLabel = GetNode<Label>("Screen/BatterRatings/ContactRow/ContactValueLabel");
        _disciplineSlider = GetNode<HSlider>("Screen/BatterRatings/DisciplineRow/DisciplineSlider");
        _disciplineValueLabel = GetNode<Label>("Screen/BatterRatings/DisciplineRow/DisciplineValueLabel");
        _pitcherRatings = GetNode<Control>("Screen/PitcherRatings");
        _stuffSlider = GetNode<HSlider>("Screen/PitcherRatings/StuffRow/StuffSlider");
        _stuffValueLabel = GetNode<Label>("Screen/PitcherRatings/StuffRow/StuffValueLabel");
        _controlSlider = GetNode<HSlider>("Screen/PitcherRatings/ControlRow/ControlSlider");
        _controlValueLabel = GetNode<Label>("Screen/PitcherRatings/ControlRow/ControlValueLabel");
        _staminaSlider = GetNode<HSlider>("Screen/PitcherRatings/StaminaRow/StaminaSlider");
        _staminaValueLabel = GetNode<Label>("Screen/PitcherRatings/StaminaRow/StaminaValueLabel");
        _fieldingSlider = GetNode<HSlider>("Screen/FieldingRow/FieldingSlider");
        _fieldingValueLabel = GetNode<Label>("Screen/FieldingRow/FieldingValueLabel");
        _remainingPointsLabel = GetNode<Label>("Screen/RemainingPointsLabel");
        _errorLabel = GetNode<Label>("Screen/ErrorLabel");
        _createButton = GetNode<Button>("Screen/CreateButton");

        PopulateTeams();
        _roleOptionButton.AddItem("Starter");
        _roleOptionButton.AddItem("Reliever");

        _batterButton.Toggled += OnCareerTypeToggled;
        _pitcherButton.Toggled += OnCareerTypeToggled;
        _nameLineEdit.TextChanged += _ => RefreshValidation();
        _powerSlider.ValueChanged += _ => RefreshRatingLabels();
        _contactSlider.ValueChanged += _ => RefreshRatingLabels();
        _disciplineSlider.ValueChanged += _ => RefreshRatingLabels();
        _stuffSlider.ValueChanged += _ => RefreshRatingLabels();
        _controlSlider.ValueChanged += _ => RefreshRatingLabels();
        _staminaSlider.ValueChanged += _ => RefreshRatingLabels();
        _fieldingSlider.ValueChanged += _ => RefreshRatingLabels();
        _rerollButton.Pressed += OnRerollPressed;
        foreach (CheckBox check in _traitCheckBoxes)
        {
            check.Toggled += OnTraitToggled;
        }
        _createButton.Pressed += OnCreatePressed;

        // Any RngState works for the reveal panel — it never touches
        // CareerManager's own stream, so re-rolling here can't perturb the
        // deterministic creation-batch draw order (arsenal generation etc.).
        _revealRng = new RngState(unchecked((ulong)System.Environment.TickCount64) | 1UL);
        RerollBackstory();
        RefreshCareerVisibility();
        RefreshRatingLabels();
        RefreshTraitAvailability();
    }

    public override void _ExitTree()
    {
        _batterButton.Toggled -= OnCareerTypeToggled;
        _pitcherButton.Toggled -= OnCareerTypeToggled;
        _rerollButton.Pressed -= OnRerollPressed;
        foreach (CheckBox check in _traitCheckBoxes)
        {
            check.Toggled -= OnTraitToggled;
        }
        _createButton.Pressed -= OnCreatePressed;
    }

    private void PopulateTeams()
    {
        // Phase 9a: every career starts on the bottom rung — the picker offers
        // only the HS tier's teams; the ladder above is 9c's promotion arc.
        var teams = new System.Collections.Generic.List<TeamRow>();
        GameManager.Instance!.Baseball.LoadTeamsByTier(LeagueTier.HS, teams);
        for (int i = 0; i < teams.Count; i++)
        {
            TeamRow team = teams[i];
            _teamOptionButton.AddItem($"{team.City} {team.Name} ({team.Abbreviation})");
            _teamOptionButton.SetItemMetadata(i, team.TeamId);
        }
    }

    private void OnRerollPressed() => RerollBackstory();

    private void RerollBackstory()
    {
        _backstory = BackstoryGenerator.Roll(ref _revealRng);
        RefreshBackstoryLabels();
    }

    private void RefreshBackstoryLabels()
    {
        string[] tierNames = WealthTierNamesCsv.Split(',');
        string[] phoneTierNames = PhoneTierNamesCsv.Split(',');
        string[] phonePlanNames = PhonePlanNamesCsv.Split(',');

        _wealthTierLabel.Text = string.Format(WealthTierFormat, tierNames[_backstory.WealthTier]);
        _householdIncomeLabel.Text = string.Format(HouseholdIncomeFormat, _backstory.HouseholdIncome);
        _startingFundsLabel.Text = string.Format(StartingFundsFormat, _backstory.StartingFunds);
        _allowanceLabel.Text = string.Format(AllowanceFormat, _backstory.AllowanceWeekly);
        _phoneLabel.Text = string.Format(
            PhoneFormat, phoneTierNames[_backstory.PhoneTier - 1], phonePlanNames[_backstory.PhonePlan]);
        _wifiLabel.Text = _backstory.HomeWifi ? WifiYesText : WifiNoText;
        _transportLabel.Text = string.Format(
            TransportFormat,
            _backstory.TransportGiftItemId is null ? TransportNoneText : HumanizeItemId(_backstory.TransportGiftItemId));
        _strictnessLabel.Text = string.Format(StrictnessFormat, _backstory.Strictness);
        _parentsLabel.Text = string.Format(
            ParentsFormat, _backstory.Parent1FirstName, _backstory.Parent1Age, _backstory.Parent2FirstName, _backstory.Parent2Age);
    }

    // HS-3's item catalog (Assets/Data/Items/items.json) doesn't exist yet, so
    // the reveal panel humanizes the catalog id directly — the same
    // fallback-humanization precedent as GrittyEventJson.Humanize.
    private static string HumanizeItemId(string itemId)
    {
        string[] parts = itemId.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
            }
        }
        return string.Join(' ', parts);
    }

    private void OnTraitToggled(bool _) => RefreshTraitAvailability();

    private void RefreshTraitAvailability()
    {
        int selected = 0;
        foreach (CheckBox check in _traitCheckBoxes)
        {
            if (check.ButtonPressed)
            {
                selected++;
            }
        }
        bool atLimit = selected >= MaxTraitPicks;
        foreach (CheckBox check in _traitCheckBoxes)
        {
            if (!check.ButtonPressed)
            {
                check.Disabled = atLimit;
            }
        }
        _traitsHintLabel.Text = string.Format(TraitsHintFormat, MaxTraitPicks, selected);
    }

    private void ApplyTraitOffsets(ref PersonRow row)
    {
        for (int i = 0; i < _traitCheckBoxes.Length; i++)
        {
            if (_traitCheckBoxes[i].ButtonPressed)
            {
                ApplyStatOffset(ref row, TraitDefs[i].StatName);
            }
        }
    }

    private static void ApplyStatOffset(ref PersonRow row, string statName)
    {
        switch (statName)
        {
            case "Teamwork":
                row.Teamwork = ClampStat(row.Teamwork + TraitStatOffset);
                break;
            case "Charisma":
                row.Charisma = ClampStat(row.Charisma + TraitStatOffset);
                break;
            case "Morality":
                row.Morality = ClampStat(row.Morality + TraitStatOffset);
                break;
            case "Reputation":
                row.Reputation = ClampStat(row.Reputation + TraitStatOffset);
                break;
            case "Discipline":
                row.Discipline = ClampStat(row.Discipline + TraitStatOffset);
                break;
            case "Maturity":
                row.Maturity = ClampStat(row.Maturity + TraitStatOffset);
                break;
        }
    }

    private static int ClampStat(int value) => Math.Clamp(value, 0, 100);

    private void OnCareerTypeToggled(bool _)
    {
        RefreshCareerVisibility();
        RefreshRatingLabels();
    }

    private void RefreshCareerVisibility()
    {
        bool isPitcher = _pitcherButton.ButtonPressed;
        _batterRatings.Visible = !isPitcher;
        _pitcherRatings.Visible = isPitcher;
        _roleRow.Visible = isPitcher;
    }

    private void RefreshRatingLabels()
    {
        _powerValueLabel.Text = ((int)_powerSlider.Value).ToString();
        _contactValueLabel.Text = ((int)_contactSlider.Value).ToString();
        _disciplineValueLabel.Text = ((int)_disciplineSlider.Value).ToString();
        _stuffValueLabel.Text = ((int)_stuffSlider.Value).ToString();
        _controlValueLabel.Text = ((int)_controlSlider.Value).ToString();
        _staminaValueLabel.Text = ((int)_staminaSlider.Value).ToString();
        _fieldingValueLabel.Text = ((int)_fieldingSlider.Value).ToString();

        RefreshValidation();
    }

    private int RemainingPoints()
    {
        bool isPitcher = _pitcherButton.ButtonPressed;
        int careerTrio = isPitcher
            ? (int)_stuffSlider.Value + (int)_controlSlider.Value + (int)_staminaSlider.Value
            : (int)_powerSlider.Value + (int)_contactSlider.Value + (int)_disciplineSlider.Value;
        return RatingBudget - careerTrio - (int)_fieldingSlider.Value;
    }

    private void RefreshValidation()
    {
        int remaining = RemainingPoints();
        _remainingPointsLabel.Text = string.Format(RemainingPointsFormat, remaining);

        bool nameValid = !string.IsNullOrWhiteSpace(_nameLineEdit.Text);
        _errorLabel.Visible = !nameValid;
        _errorLabel.Text = nameValid ? string.Empty : NameRequiredText;
        _createButton.Disabled = remaining != 0 || !nameValid;
    }

    private void OnCreatePressed()
    {
        GameManager gm = GameManager.Instance!;
        bool isPitcher = _pitcherButton.ButtonPressed;
        int teamId = _teamOptionButton.GetItemMetadata(_teamOptionButton.Selected).AsInt32();

        var ratings = new PlayerRatingsRow
        {
            IsPitcher = isPitcher,
            BatPower = isPitcher ? 50 : (int)_powerSlider.Value,
            BatContact = isPitcher ? 50 : (int)_contactSlider.Value,
            BatDiscipline = isPitcher ? 50 : (int)_disciplineSlider.Value,
            PitStuff = isPitcher ? (int)_stuffSlider.Value : 50,
            PitControl = isPitcher ? (int)_controlSlider.Value : 50,
            PitStamina = isPitcher ? (int)_staminaSlider.Value : 50,
            Fielding = (int)_fieldingSlider.Value,
        };
        PitcherRole role = _roleOptionButton.Selected == 1 ? PitcherRole.Reliever : PitcherRole.Starter;

        // PlayerId is a placeholder — CreateAvatar's household seeding
        // overwrites it with the freshly generated avatar id once the row is
        // inside the creation batch (CareerManager.SeedFoundingHousehold).
        PersonRow personSeed = BackstoryGenerator.BuildPersonRow(string.Empty, in _backstory);
        ApplyTraitOffsets(ref personSeed);

        try
        {
            gm.Career.CreateAvatar(_nameLineEdit.Text.Trim(), string.Empty, teamId, in ratings, in _backstory, personSeed, role);
        }
        catch (System.Exception ex)
        {
            _errorLabel.Text = ex.Message;
            _errorLabel.Visible = true;
            return;
        }

        string avatarId = gm.Career.AvatarPlayerId;
        long day = gm.State.CurrentDay;
        for (int i = 0; i < _traitCheckBoxes.Length; i++)
        {
            if (_traitCheckBoxes[i].ButtonPressed)
            {
                gm.Players.SetFlag(avatarId, TraitDefs[i].FlagName, true, day);
            }
        }

        _errorLabel.Visible = false;
        EmitSignal(SignalName.AvatarCreated);
    }
}
