using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// New-game avatar creation: name, team, batter-or-pitcher career (with a
/// bullpen role sub-choice for pitchers), and a fixed-budget ratings
/// allocator over the three career-specific attributes plus fielding. Calls
/// straight into <see cref="DirtAndDiamonds.Simulation.Baseball.CareerManager.CreateAvatar"/>
/// on submit, which owns every side effect (teammate benching, arsenal
/// generation, sim re-init) — this screen only collects and validates input.
/// UI never touches the database directly, per ui_conventions.
/// Node paths verified against NewGameScreen.tscn via godot_scene_mapper
/// before this script was written.
/// </summary>
public sealed partial class NewGameScreen : Control
{
    [Signal]
    public delegate void AvatarCreatedEventHandler();

    [Export]
    public string RemainingPointsFormat { get; set; } = "Points remaining: {0}";

    [Export]
    public string NameRequiredText { get; set; } = "Enter a name before starting your career.";

    // Every rookie starts at league-average (50) on all seven ratings; the
    // budget below equals the four relevant sliders' combined baseline
    // (4 * 50), so allocation starts valid and every reassignment is a
    // trade-off rather than a free bonus.
    private const int MinRating = 20;
    private const int MaxRating = 90;
    private const int RatingBudget = 200;

    private LineEdit _nameLineEdit = null!;
    private OptionButton _teamOptionButton = null!;
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

    public override void _Ready()
    {
        _nameLineEdit = GetNode<LineEdit>("Screen/NameRow/NameLineEdit");
        _teamOptionButton = GetNode<OptionButton>("Screen/TeamRow/TeamOptionButton");
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
        _createButton.Pressed += OnCreatePressed;

        RefreshCareerVisibility();
        RefreshRatingLabels();
    }

    public override void _ExitTree()
    {
        _batterButton.Toggled -= OnCareerTypeToggled;
        _pitcherButton.Toggled -= OnCareerTypeToggled;
        _createButton.Pressed -= OnCreatePressed;
    }

    private void PopulateTeams()
    {
        var teams = new System.Collections.Generic.List<TeamRow>();
        GameManager.Instance!.Baseball.LoadAllTeams(teams);
        for (int i = 0; i < teams.Count; i++)
        {
            TeamRow team = teams[i];
            _teamOptionButton.AddItem($"{team.City} {team.Name} ({team.Abbreviation})");
            _teamOptionButton.SetItemMetadata(i, team.TeamId);
        }
    }

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

        try
        {
            gm.Career.CreateAvatar(_nameLineEdit.Text.Trim(), string.Empty, teamId, in ratings, role);
        }
        catch (System.Exception ex)
        {
            _errorLabel.Text = ex.Message;
            _errorLabel.Visible = true;
            return;
        }

        _errorLabel.Visible = false;
        EmitSignal(SignalName.AvatarCreated);
    }
}
