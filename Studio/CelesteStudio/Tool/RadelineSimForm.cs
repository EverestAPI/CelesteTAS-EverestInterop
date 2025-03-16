using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CelesteStudio.Communication;
using CelesteStudio.Data;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Tool;

public sealed class RadelineSimForm : Form {
    private const string Version = "0.0.1";

    private readonly TextArea initialStateBox;
    private readonly TextArea appendKeys;
    private readonly TextArea log;
    private readonly Button getInitialState;
    private readonly Button run;
    private readonly ProgressBar progressBar;
    private readonly DropDown outputSorting;
    private readonly DropDown axis;
    private readonly DropDown disabledKey;
    private readonly NumericStepper frames;
    private readonly NumericStepper inputGenerationTime;
    private readonly NumericStepper positionFilterMin;
    private readonly NumericStepper positionFilterMax;
    private readonly NumericStepper goalSpeed;
    private readonly NumericStepper rngThresholdSlow;
    private readonly NumericStepper rngThreshold;
    private readonly CheckBox hideDuplicates;
    private readonly ListBox outputs;
    private readonly Label progressType;

    private static InitialState initialState;
    private static SimConfig cfg;

    public RadelineSimForm() {
        Title = $"Radeline Simulator - v{Version}";
        Icon = Assets.AppIcon;

        Menu = new MenuBar {
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
                Studio.ShowAboutDialog(new AboutDialog {
                    ProgramName = "Radeline Simulator",
                    ProgramDescription = "Basic movement simulator, for brute forcing precise position/speed values. Hover over most labels for tooltips",
                    Version = Version,

                    Developers = ["Kataiser"],
                    Logo = Icon,
                }, this);
            }),
        };

        const int rowWidth = 180;
        const string positionFilterTooltip = "Only show results within this position range (min and max can be backwards, won't make a difference)";

        initialStateBox = new TextArea { ReadOnly = true, Wrap = true, Font = FontManager.StatusFont, Width = 180, Height = 190};
        getInitialState = new Button((_, _) => SetInitialStateTesting()) { Text = "Get Initial State", Width = 150 };
        log = new TextArea { ReadOnly = true, Wrap = true, Font = FontManager.StatusFont, Width = rowWidth };
        outputSorting = new DropDown {
            Items = {
                new ListItem { Text = "Position" },
                new ListItem { Text = "Speed" }
            },
            SelectedKey = "Position",
            Width = rowWidth
        };
        axis = new DropDown {
            Items = {
                new ListItem { Text = "X (horizontal)", Key = "x" },
                new ListItem { Text = "Y (vertical)", Key = "y" }
            },
            SelectedKey = "x",
            Width = rowWidth
        };
        disabledKey = new DropDown {
            Items = {
                new ListItem { Text = "Auto" },
                new ListItem { Text = "L" },
                new ListItem { Text = "R" },
                new ListItem { Text = "J" },
                new ListItem { Text = "D" }
            },
            SelectedKey = "Auto",
            Width = rowWidth
        };
        frames = new NumericStepper { Value = 10, MinValue = 1, MaxValue = 200, Width = rowWidth };
        inputGenerationTime = new NumericStepper { Value = 10, MinValue = 1, MaxValue = 200, Width = rowWidth };
        positionFilterMin = new NumericStepper { DecimalPlaces = CommunicationWrapper.GameSettings.PositionDecimals, Width = rowWidth };
        positionFilterMax = new NumericStepper { DecimalPlaces = CommunicationWrapper.GameSettings.PositionDecimals, Width = rowWidth };
        goalSpeed = new NumericStepper { DecimalPlaces = CommunicationWrapper.GameSettings.PositionDecimals, Width = rowWidth };
        rngThreshold = new NumericStepper { Value = 20, MinValue = 1, MaxValue = 200, Width = rowWidth };
        rngThresholdSlow = new NumericStepper { Value = 14, MinValue = 1, MaxValue = 200, Width = rowWidth };
        appendKeys = new TextArea { Font = FontManager.EditorFont, Width = rowWidth, Height = 22};
        hideDuplicates = new CheckBox { Width = rowWidth };
        outputs = new ListBox { Font = FontManager.StatusFont, Width = 500 };
        progressBar = new ProgressBar { Width = 300 };
        progressType = new Label();

        outputs.SelectedIndexChanged += OutputsOnSelectedIndexChanged;

        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 8) };
        layout.BeginHorizontal();

        layout.BeginVertical();
        layout.BeginHorizontal();
        layout.Add(initialStateBox);
        layout.Add(log);
        layout.EndBeginHorizontal();
        layout.Add(getInitialState);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Frames", ToolTip = "Number of frames to simulate" });
        layout.Add(frames);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Axis" });
        layout.Add(axis);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Position Filter (Min)", ToolTip = positionFilterTooltip });
        layout.Add(positionFilterMin);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Position Filter (Max)", ToolTip = positionFilterTooltip });
        layout.Add(positionFilterMax);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Goal Speed", ToolTip = "This is calculated with |final speed - goal speed|" });
        layout.Add(goalSpeed);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Disabled Key", ToolTip = "Disable generating a certain key. Auto will disable keys that can't ever affect input" });
        layout.Add(disabledKey);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Output Sorting Priority" });
        layout.Add(outputSorting);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "RNG Threshold (All Keys)", ToolTip = "Frame count at which to start using the RNG input generation method " +
                                                                                    "(instead of sequential) when all keys are enabled"});
        layout.Add(rngThresholdSlow);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "RNG Threshold (Disabled Key)", ToolTip = "Frame count at which to start using the RNG input generation method " +
                                                                                        "(instead of sequential) when a key is disabled" });
        layout.Add(rngThreshold);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Input Generation Time", ToolTip = "How long to spend generating random inputs, in seconds"});
        layout.Add(inputGenerationTime);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Appended Keys", ToolTip = "Keys the formatter adds, e.g. \"jg\" to hold jump and grab as well"});
        layout.Add(appendKeys);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Hide Duplicate Inputs", ToolTip = "Don't output multiple inputs with the same resulting position/speed (disable for performance)"});
        layout.Add(hideDuplicates);
        layout.EndBeginHorizontal();
        layout.EndHorizontal();
        layout.EndVertical();

        layout.Add(outputs);

        layout.EndHorizontal();

        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items = {
                layout,
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    Items = {
                        (run = new Button((_, _) => Run()) { Text = "Run", Width = 150 }),
                        progressType,
                        progressBar
                    }
                }
            }
        };

        Resizable = false;
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
    }

    private void Log(string message) {
        log.Text += $"{message}\n";
    }

    #region Algorithm

    private void Run() {
        SetupSimConfig();

        if (cfg.DisabledKey == DisabledKey.Auto) {
            cfg.DisabledKey = DisabledKey.None;

            // do some math to determine if a key can ever affect movement
            if (cfg.Axis == Axis.X) {
                // disable holding backwards if speed can't ever drop below zero due to friction
                bool speedHighEnoughNotGrounded = !initialState.OnGround && Math.Abs(initialState.Speed) > cfg.Frames * 65f / 6f;
                bool speedHighEnoughGrounded = initialState.OnGround && Math.Abs(initialState.Speed) > cfg.Frames * 50f / 3f;

                if (speedHighEnoughNotGrounded || speedHighEnoughGrounded) {
                    cfg.DisabledKey = initialState.Speed > 0f ? DisabledKey.L : DisabledKey.R;
                }
            } else {
                // disable jump if past jump peak, or down if can't ever reach fast fall speed
                if (initialState.Speed > 40f) {
                    cfg.DisabledKey = DisabledKey.J;
                } else if (initialState.Speed + cfg.Frames * 15f <= 160) {
                    cfg.DisabledKey = DisabledKey.D;
                }
            }
        } else {
            bool cantDisableX = cfg.Axis == Axis.X && cfg.DisabledKey == DisabledKey.J;
            bool cantDisableY = cfg.Axis == Axis.Y && new[] {DisabledKey.L, DisabledKey.R}.Contains(cfg.DisabledKey);

            if (cantDisableX || cantDisableY) {
                Log($"Didn't disable {cfg.DisabledKey} key since it wouldn't have been generated anyway");
                cfg.DisabledKey = DisabledKey.None;
            }
        }

        if (cfg.DisabledKey != DisabledKey.None) {
            Log($"Disabled generating {cfg.DisabledKey} inputs");
        }

        outputs.Items.Clear();
        outputs.Items.Add(new ListItem { Text = "(574.000041, 127.500360) 20,J 7,D"});
        outputs.Items.Add(new ListItem { Text = "(574.000041, 120.000340) 14,J 1 12,J"});
    }

    // TODO: implement
    private void SetInitialState() {
        if (!CommunicationWrapper.Connected) {
            return;
        }

        initialState = new InitialState {
            Positions = (CommunicationWrapper.PlayerPosition.X + CommunicationWrapper.PlayerPositionRemainder.X,
                CommunicationWrapper.PlayerPosition.Y + CommunicationWrapper.PlayerPositionRemainder.Y)
        };

        initialState.Positions.X += CommunicationWrapper.PlayerPositionRemainder.X;

        initialStateBox.Text = initialState.Position.ToString();
    }

    private void SetInitialStateTesting() {
        initialState = new InitialState {
            Positions = (160.458372503519f, 67.7499865889549f),
            Speeds = (0f, -15f),
            OnGround = false,
            Holding = false,
            JumpTimer = 0,
            AutoJump = false,
            MaxFall = 160f
        };

        initialStateBox.Text = "Pos: 160.458372503519, 67.7499865889549\nSpeed: 0.00, -15.00\nVel: 0.00, 0.00\nStamina: 110 Timer: 16.609\n" +
                               "Dash\n[1]\n\nAutoJump: False\nMaxFall: 160.00\nJumpTimer: 0\nHolding:";
    }

    private void OutputsOnSelectedIndexChanged(object? sender, EventArgs e) {
        var selectedItem = outputs.Items[outputs.SelectedIndex];
        Clipboard.Instance.Clear();
        Clipboard.Instance.Text = selectedItem.Text;
    }

    private void SetupSimConfig() {
        float positionFilterMinValue = (float) positionFilterMin.Value;
        float positionFilterMaxValue = (float) positionFilterMax.Value;

        // fix for accidentally backwards order
        if (positionFilterMinValue > positionFilterMaxValue) {
            (positionFilterMinValue, positionFilterMaxValue) = (positionFilterMaxValue, positionFilterMinValue);
        }

        Enum.TryParse(axis.SelectedKey, out Axis axisSelected);
        Enum.TryParse(disabledKey.SelectedKey, out DisabledKey disabledKeySelected);
        Enum.TryParse(outputSorting.SelectedKey, out OutputSortingPriority outputSortingPrioritySelected);

        cfg = new SimConfig {
            Frames = (int) frames.Value,
            Axis = axisSelected,
            PositionFilter = (positionFilterMinValue, positionFilterMaxValue),
            GoalSpeed = (float) goalSpeed.Value,
            DisabledKey = disabledKeySelected,
            OutputSortingPriority = outputSortingPrioritySelected,
            RNGThresholdSlow = (int) rngThresholdSlow.Value,
            RNGThreshold = (int) rngThreshold.Value,
            AppendKeys = appendKeys.Text,
            HideDuplicateInputs = hideDuplicates.Checked!.Value
        };

        initialState.Position = axisSelected == Axis.X ? initialState.Positions.X : initialState.Positions.Y;
        initialState.Speed = axisSelected == Axis.X ? initialState.Speeds.X : initialState.Speeds.Y;
    }

    private struct InitialState {
        public (float X, float Y) Positions;
        public (float X, float Y) Speeds;
        // X axis:
        public bool OnGround;
        public bool Holding;
        // Y axis:
        public int JumpTimer;
        public bool AutoJump;
        public float MaxFall;
        // finalized:
        public float Position;
        public float Speed;
    }

    private struct SimConfig {
        public int Frames;
        public Axis Axis;
        public (float Min, float Max) PositionFilter;
        public float GoalSpeed;
        public DisabledKey DisabledKey;
        public OutputSortingPriority OutputSortingPriority;
        public int RNGThresholdSlow;
        public int RNGThreshold;
        public string AppendKeys;
        public bool HideDuplicateInputs;
    }

    private enum Axis { X, Y }
    private enum DisabledKey { None, Auto, L, R, J, D }
    private enum OutputSortingPriority { Position, Speed }

    #endregion
}
