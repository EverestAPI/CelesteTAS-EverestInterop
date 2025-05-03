using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Tedd.RandomUtils;
using InputPermutation = System.Collections.Generic.List<(int Frames, CelesteStudio.Tool.RadelineSimForm.InputAction Action)>;

namespace CelesteStudio.Tool;

public sealed class RadelineSimForm : Form {
    [Flags]
    public enum InputAction {
        None = 1 << 0,
        Left = 1 << 1,
        Right = 1 << 2,
        Down = 1 << 3,
        Jump = 1 << 4,
    }
    private static char CharForAction(InputAction action) {
        return action switch {
            InputAction.None => ' ',
            InputAction.Left => 'L',
            InputAction.Right => 'R',
            InputAction.Down => 'D',
            InputAction.Jump => 'J',
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private const string Version = "1.0.0";

    private readonly TextArea initialStateText;
    private readonly TextBox additionalInputsText;
    private readonly DropDown outputSortingOption;
    private readonly DropDown axisOption;
    private readonly DropDown disabledKeyOption;
    private readonly NumericStepper framesStepper;
    private readonly NumericStepper inputGenerationTimeStepper;
    private readonly NumericStepper positionFilterMinStepper;
    private readonly NumericStepper positionFilterMaxStepper;
    private readonly NumericStepper goalSpeedStepper;
    private readonly NumericStepper rngThresholdSlowStepper;
    private readonly NumericStepper rngThresholdStepper;
    private readonly CheckBox hideDuplicatesCheck;

    private const string InvalidOutput = "InvalidOutput";
    private readonly ListBox outputsList;

    private Eto.Forms.Dialog? progressPopup;
    private TextArea logControl = null!;
    private ProgressBar individualProgressBar = null!;

    private readonly Config cfg;
    private InitialState initialState;

    private InputAction availableActions = 0;
    private bool gotInitialState;
    private bool isRunning;
    private readonly List<Control> disabledControls;

    public RadelineSimForm(Config formPersistence) {
        cfg = formPersistence;

        Title = $"Radeline Simulator - v{Version}";
        Icon = Assets.AppIcon;

        Menu = new MenuBar {
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
                Studio.ShowAboutDialog(new AboutDialog {
                    ProgramName = "Radeline Simulator",
                    ProgramDescription = "Basic movement simulator, for brute forcing precise position/speed values. Hover over most labels for tooltips.",
                    Version = Version,

                    Developers = ["Kataiser"],
                    Logo = Icon,
                }, this);
            }),
        };

        const int rowWidth = 180;

        var runButton = new Button((_, _) => Run()) { Text = "Run", Width = 150 };
        if (!gotInitialState) {
            runButton.Enabled = false;
            runButton.ToolTip = "No initial state";
        } else if (initialState.PlayerStateName != "StNormal") {
            runButton.Enabled = false;
            runButton.ToolTip = $"Player state must be StNormal, not {initialState.PlayerStateName}";
        }

        initialStateText = new TextArea { ReadOnly = true, Wrap = true, Font = FontManager.StatusFont, Width = 180, Height = 190};
        var getInitialStateButton = new Button((_, _) => {
            FetchInitialState();

            if (!gotInitialState) {
                runButton.Enabled = false;
                runButton.ToolTip = "No initial state";
            } else if (initialState.PlayerStateName != "StNormal") {
                runButton.Enabled = false;
                runButton.ToolTip = $"Player state must be StNormal, not {initialState.PlayerStateName}";
            } else {
                runButton.Enabled = true;
                runButton.ToolTip = string.Empty;
            }
        }) { Text = "Get Initial State", Width = 150 };

        if (!CommunicationWrapper.Connected) {
            getInitialStateButton.Enabled = false;
            getInitialStateButton.ToolTip = "Not connected to game";
        }
        CommunicationWrapper.ConnectionChanged += () => {
            if (!CommunicationWrapper.Connected) {
                getInitialStateButton.Enabled = false;
                getInitialStateButton.ToolTip = "Not connected to game";
            } else {
                getInitialStateButton.Enabled = true;
                getInitialStateButton.ToolTip = string.Empty;
            }
        };

        outputSortingOption = new DropDown {
            Items = {
                new ListItem { Text = "Position" },
                new ListItem { Text = "Speed" }
            },
            SelectedKey = "Position",
            Width = rowWidth
        };
        axisOption = new DropDown {
            Items = {
                new ListItem { Text = "X (horizontal)", Key = "X" },
                new ListItem { Text = "Y (vertical)", Key = "Y" }
            },
            SelectedKey = "X",
            Width = rowWidth
        };
        disabledKeyOption = new DropDown {
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
        framesStepper = new NumericStepper { Value = 10, MinValue = 1, MaxValue = 200, Width = rowWidth };
        inputGenerationTimeStepper = new NumericStepper { Value = 5, MinValue = 1, MaxValue = 200, Width = rowWidth };
        positionFilterMinStepper = new NumericStepper { DecimalPlaces = 2, Width = rowWidth };
        positionFilterMaxStepper = new NumericStepper { DecimalPlaces = 2, Width = rowWidth };
        goalSpeedStepper = new NumericStepper { DecimalPlaces = 2, Width = rowWidth };
        rngThresholdStepper = new NumericStepper { Value = 23, MinValue = 1, MaxValue = 200, Width = rowWidth };
        rngThresholdSlowStepper = new NumericStepper { Value = 15, MinValue = 1, MaxValue = 200, Width = rowWidth };
        additionalInputsText = new TextBox { Font = FontManager.EditorFont, Width = rowWidth };
        hideDuplicatesCheck = new CheckBox { Width = rowWidth, Checked = true };
        outputsList = new ListBox { Font = FontManager.StatusFont, Width = 600, Height = 500 };
        outputsList.SelectedIndexChanged += CopySelectedOutput;

        const string positionFilterTooltip = "Only show results within this position range (min and max can be backwards, won't make a difference)";

        var configLayout = new DynamicLayout { DefaultSpacing = new Size(10, 8) };

        configLayout.BeginVertical();
        configLayout.BeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Frames", ToolTip = "Number of frames to simulate" });
        configLayout.Add(framesStepper);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Axis" });
        configLayout.Add(axisOption);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Position Filter (Min)", ToolTip = positionFilterTooltip });
        configLayout.Add(positionFilterMinStepper);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Position Filter (Max)", ToolTip = positionFilterTooltip });
        configLayout.Add(positionFilterMaxStepper);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Goal Speed", ToolTip = "This is calculated with |final speed - goal speed|" });
        configLayout.Add(goalSpeedStepper);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Disabled Key", ToolTip = "Disable generating a certain key. Auto will disable keys that can't ever affect input" });
        configLayout.Add(disabledKeyOption);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "RNG Threshold (All Keys)", ToolTip = "Frame count at which to start using the RNG input generation method (instead of sequential) when all keys are enabled"});
        configLayout.Add(rngThresholdSlowStepper);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "RNG Threshold (Disabled Key)", ToolTip = "Frame count at which to start using the RNG input generation method (instead of sequential) when a key is disabled" });
        configLayout.Add(rngThresholdStepper);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Input Generation Time (RNG)", ToolTip = "How long to spend generating random inputs, in seconds"});
        configLayout.Add(inputGenerationTimeStepper);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Output Sorting Priority" });
        configLayout.Add(outputSortingOption);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Additional Inputs", ToolTip = "Keys added when copying, e.g. \"jg\" to hold jump and grab as well"});
        configLayout.Add(additionalInputsText);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Hide Duplicate Inputs", ToolTip = "Don't output multiple inputs with the same resulting position/speed (disable for performance)"});
        configLayout.Add(hideDuplicatesCheck);
        configLayout.EndBeginHorizontal();
        configLayout.EndHorizontal();
        configLayout.EndVertical();

        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 8) };
        layout.BeginHorizontal();

        layout.BeginVertical();
        layout.Add(initialStateText);
        layout.Add(getInitialStateButton);
        layout.Add(configLayout);
        layout.EndVertical();

        layout.Add(outputsList);

        layout.EndHorizontal();

        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items = {
                layout,
                runButton
            }
        };
        Resizable = false;

        Studio.RegisterWindow(this);

        Closed += (_, _) => StoreConfig();
        LoadConfig();

        disabledControls = [getInitialStateButton, framesStepper, axisOption, positionFilterMinStepper, positionFilterMaxStepper, goalSpeedStepper, disabledKeyOption,
            outputSortingOption, rngThresholdStepper, rngThresholdSlowStepper, inputGenerationTimeStepper, additionalInputsText, hideDuplicatesCheck];
    }

    private void StoreConfig() {
        float positionFilterMinValue = (float) positionFilterMinStepper.Value;
        float positionFilterMaxValue = (float) positionFilterMaxStepper.Value;

        // fix for accidentally backwards order
        if (positionFilterMinValue > positionFilterMaxValue) {
            (positionFilterMinValue, positionFilterMaxValue) = (positionFilterMaxValue, positionFilterMinValue);
        }

        cfg.Frames = (int) framesStepper.Value;
        cfg.Axis = Enum.Parse<Axis>(axisOption.SelectedKey);
        cfg.PositionFilter = (positionFilterMinValue, positionFilterMaxValue);
        cfg.GoalSpeed = (float) goalSpeedStepper.Value;
        cfg.DisabledKey = Enum.Parse<DisabledKey>(disabledKeyOption.SelectedKey);
        cfg.OutputSortingPriority = Enum.Parse<OutputSortingPriority>(outputSortingOption.SelectedKey);
        cfg.RNGThresholdSlow = (int) rngThresholdSlowStepper.Value;
        cfg.RNGThreshold = (int) rngThresholdStepper.Value;
        cfg.HideDuplicateInputs = hideDuplicatesCheck.Checked!.Value;
        cfg.InputGenerationTime = (int) inputGenerationTimeStepper.Value * 1000;
        cfg.AdditionalInputs = additionalInputsText.Text;
        cfg.InitialState = initialState;
        cfg.InitialStateInfo = initialStateText.Text;

        initialState.Position = cfg.Axis == Axis.X ? initialState.Positions.X : initialState.Positions.Y;
        initialState.Speed = cfg.Axis == Axis.X ? initialState.Speeds.X : initialState.Speeds.Y;
    }
    private void LoadConfig() {
        if (cfg.WindowFirstOpen) {
            cfg.WindowFirstOpen = false;
            return;
        }

        framesStepper.Value = cfg.Frames;
        axisOption.SelectedKey = cfg.Axis.ToString();
        positionFilterMinStepper.Value = cfg.PositionFilter.Min;
        positionFilterMaxStepper.Value = cfg.PositionFilter.Max;
        goalSpeedStepper.Value = cfg.GoalSpeed;
        disabledKeyOption.SelectedKey = cfg.DisabledKey.ToString();
        outputSortingOption.SelectedKey = cfg.OutputSortingPriority.ToString();
        rngThresholdStepper.Value = cfg.RNGThreshold;
        rngThresholdSlowStepper.Value = cfg.RNGThresholdSlow;
        inputGenerationTimeStepper.Value = cfg.InputGenerationTime / 1000;
        additionalInputsText.Text = cfg.AdditionalInputs;
        hideDuplicatesCheck.Checked = cfg.HideDuplicateInputs;
        initialStateText.Text = cfg.InitialStateInfo;

        initialState = cfg.InitialState;
        gotInitialState = true;
    }

    private void Run() {
        var doneCancelButton = new Button((_, _) => progressPopup?.Close()) { Text = "Cancel", Width = 200 };

        progressPopup = new Eto.Forms.Dialog {
            Title = "Simulating...",
            Icon = Assets.AppIcon,

            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = {
                    (logControl = new TextArea { ReadOnly = true, Wrap = true, Font = FontManager.StatusFont, Width = 500, Height = 125 }),
                    (individualProgressBar = new ProgressBar { Width = 500 }),
                    doneCancelButton,
                },
            },

            Resizable = false,
            Closeable = false,
            ShowInTaskbar = true,
        };
        progressPopup.Closed += (_, _) => {
            isRunning = false;
            foreach (var control in disabledControls) {
                control.Enabled = true;
            }

            progressPopup = null;
        };

        Studio.RegisterDialog(progressPopup, this);

        isRunning = true;
        foreach (var control in disabledControls) {
            control.Enabled = false;
        }
        Task.Run(async () => {
            bool autoClose;
            try {
                await RunSim();
                autoClose = true;
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                await Log(ex.ToString());
                autoClose = false;
            }

            await Application.Instance.InvokeAsync(() => doneCancelButton.Text = "Done");

            // Auto-close after 3s
            if (autoClose) {
                await Task.Delay(TimeSpan.FromSeconds(3.0f));
                await Application.Instance.InvokeAsync(() => progressPopup?.Close());
            }
        });

        progressPopup.ShowModal();
    }

    private async Task Log(string message) {
        await Application.Instance.InvokeAsync(() => {
            logControl.Text += $"- {message}\n";
            logControl.ScrollToEnd();
        });
    }

    private async Task RunSim() {
        await Application.Instance.InvokeAsync(() => {
            StoreConfig();
            individualProgressBar.Value = 0;
        });

        if (cfg.DisabledKey == DisabledKey.Auto) {
            cfg.DisabledKey = DisabledKey.None;

            // do some math to determine if a key can ever affect movement
            if (cfg.Axis == Axis.X) {
                // disable holding backwards if speed can't ever drop below zero due to friction
                bool speedHighEnoughNotGrounded = !initialState.OnGround && Math.Abs(initialState.Speed) > cfg.Frames * (65f / 6f);
                bool speedHighEnoughGrounded = initialState.OnGround && Math.Abs(initialState.Speed) > cfg.Frames * (50f / 3f);

                if (speedHighEnoughNotGrounded || speedHighEnoughGrounded) {
                    cfg.DisabledKey = initialState.Speed > 0f ? DisabledKey.L : DisabledKey.R;
                }
            } else {
                // disable jump if past jump peak, or down if can't ever reach fast fall speed
                if (initialState.Speed > 40f || initialState.AutoJump) {
                    cfg.DisabledKey = DisabledKey.J;
                } else if (initialState.Speed + cfg.Frames * 15f <= 160) {
                    cfg.DisabledKey = DisabledKey.D;
                }
            }
        } else {
            bool cantDisableX = cfg.Axis == Axis.X && cfg.DisabledKey == DisabledKey.J;
            bool cantDisableY = cfg.Axis == Axis.Y && new[] {DisabledKey.L, DisabledKey.R}.Contains(cfg.DisabledKey);

            if (cantDisableX || cantDisableY) {
                await Log($"Didn't disable {cfg.DisabledKey} key since it wouldn't have been generated anyway");
                cfg.DisabledKey = DisabledKey.None;
            }
        }

        if (cfg.DisabledKey != DisabledKey.None) {
            await Log($"Disabled generating {cfg.DisabledKey} inputs");
        }

        int rngThreshold = BitOperations.PopCount((uint) (availableActions = GenerateAvailableActions())) switch {
            3 => cfg.RNGThresholdSlow,
            4 => cfg.RNGThresholdSlow - 2,
            _ => cfg.RNGThreshold
        };

        ICollection<InputPermutation> inputPermutations;
        if (cfg.Frames < rngThreshold) {
            await Log("Generating permutations using sequential method...");
            inputPermutations = await GenerateInputPermutationsSequential();
        } else {
            await Log("Generating permutations using RNG method...");
            inputPermutations = await GenerateInputPermutationsRng();
        }

        if (!isRunning) {
            return; // canceled
        }

        await Log($"Generated permutations: {inputPermutations.Count}");
        await Application.Instance.InvokeAsync(UpdateLayout);

        // store as position and speed dicts, for performance
        var filteredPermutations = new SortedDictionary<float, Dictionary<float, List<InputPermutation>>>();
        var speeds = new HashSet<float>();

        Func<InputPermutation, (float position, float speed)> simFunction = cfg.Axis == Axis.X ? SimX : SimY;

        await Application.Instance.InvokeAsync(() => individualProgressBar.Value = inputPermutations.Count);
        await Log("Simulating inputs...");

        int updateInterval = 10000 / cfg.Frames;
        int i = 0;
        foreach (var permutation in inputPermutations) {
            var simResult = simFunction(permutation);
            i++;

            // if result within filter range
            if (simResult.position >= cfg.PositionFilter.Min && simResult.position <= cfg.PositionFilter.Max) {
                filteredPermutations.TryAdd(simResult.position, []);

                if (filteredPermutations[simResult.position].TryGetValue(simResult.speed, out var prevPermutations)) {
                    bool appendPermutation = true;

                    if (cfg.HideDuplicateInputs) {
                        int removedCount = prevPermutations.RemoveAll(prevPermutation => permutation.Count < prevPermutation.Count);
                        appendPermutation = removedCount != 0;
                    }

                    if (appendPermutation) {
                        prevPermutations.Add(permutation);
                        speeds.Add(simResult.speed);
                    }
                } else {
                    filteredPermutations[simResult.position][simResult.speed] = [permutation];
                    speeds.Add(simResult.speed);
                }
            }

            if (i % updateInterval == 0) {
                await Application.Instance.InvokeAsync(() => {
                    individualProgressBar.Value = i;
                    UpdateLayout();
                });

                if (!isRunning) {
                    return;  // canceled
                }
            }
        }

        List<(float position, float speed, InputPermutation inputs)> outputPermutations = [];

        // convert optimized dict to sorted list
        if (cfg.OutputSortingPriority == OutputSortingPriority.Position) {
            foreach (var positionPair in filteredPermutations) {
                foreach (var speedPair in positionPair.Value.OrderByDescending(s => Math.Abs(s.Key - cfg.GoalSpeed))) {
                    foreach (var permutation in speedPair.Value) {
                        outputPermutations.Add((positionPair.Key, speedPair.Key, permutation));
                    }
                }
            }
        } else {
            foreach (float speed in speeds.ToList().OrderByDescending(s => Math.Abs(s - cfg.GoalSpeed))) {
                foreach (float position in filteredPermutations.Keys) {
                    if (filteredPermutations[position].TryGetValue(speed, out var permutations)) {
                        foreach (var permutation in permutations) {
                            outputPermutations.Add((position, speed, permutation));
                        }
                    }
                }
            }
        }

        await Log($"Filtered permutations: {outputPermutations.Count}");
        await Application.Instance.InvokeAsync(() => {
            outputsList.Items.Clear();
            UpdateLayout();
        });
        i = 0;

        // insert results into output window
        foreach (var outputPermutation in outputPermutations) {
            await Application.Instance.InvokeAsync(() => {
                outputsList.Items.Add(new ListItem { Text = FormatInputPermutationText(outputPermutation), Key = FormatInputPermutationKey(outputPermutation.inputs) });
            });
            i++;

            if (i == 1000) {
                await Application.Instance.InvokeAsync(UpdateLayout);
                i = 0;
            }
        }

        await Application.Instance.InvokeAsync(() => {
            individualProgressBar.Value = individualProgressBar.MaxValue;

            if (outputsList.Items.Count == 0) {
                outputsList.Items.Add(new ListItem { Text = "No solution found", Key = InvalidOutput });
            }

            UpdateLayout();
        });

        await Log("Complete");
    }

    #region Permutations

    /// Enumerates all permutations with the specified length
    private static IEnumerable<IEnumerable<T>> EnumeratePermutations<T>(T[] source, int length) {
        IEnumerable<IEnumerable<T>> seed = [[]];

        for (int i = 0; i < length; i++) {
            seed = seed.SelectMany(_ => source, (seq, item) => seq.Concat([item]));
        }

        return seed;
    }

    private async Task<ICollection<InputPermutation>> GenerateInputPermutationsSequential() {
        var actions = availableActions.ToValues().ToArray();
        int expectedPermutations = (int) Math.Pow(actions.Length, cfg.Frames);

        List<InputPermutation> inputPermutations = new(expectedPermutations);
        await Application.Instance.InvokeAsync(() => individualProgressBar.MaxValue = expectedPermutations);

        const int updateInterval = 10000;

        int i = 0;
        foreach (var permutation in EnumeratePermutations(actions, cfg.Frames)) {
            i++;

            InputPermutation permutationFormatted = [];
            var currentInput = InputAction.None;
            int inputLen = 0;

            // convert messy inputs to the compact format
            foreach (var key in permutation) {
                if (currentInput == InputAction.None) {
                    currentInput = key;
                }

                if (key == currentInput) {
                    inputLen++;
                } else {
                    permutationFormatted.Add((inputLen, currentInput));
                    currentInput = key;
                    inputLen = 1;
                }
            }

            permutationFormatted.Add((inputLen, currentInput));
            inputPermutations.Add(permutationFormatted);

            if (i % updateInterval == 0) {
                await Application.Instance.InvokeAsync(() => {
                    individualProgressBar.Value = i;
                    UpdateLayout();
                });

                if (!isRunning) {
                    return [];  // canceled
                }
            }
        }

        return inputPermutations;
    }

    private async Task<HashSet<InputPermutation>> GenerateInputPermutationsRng() {
        var inputPermutations = new HashSet<InputPermutation>(new InputPermutationComparer());
        await Application.Instance.InvokeAsync(() => individualProgressBar.MaxValue = cfg.InputGenerationTime);

        var actions = availableActions.ToValues().ToArray();

        int updateInterval = 1000000 / cfg.Frames;
        var random = new FastRandom();
        var stopwatch = Stopwatch.StartNew();

        double maxPermutationsDouble = Math.Pow(actions.Length, cfg.Frames);

        int maxPermutations;
        bool useMaxPermutations;
        if (maxPermutationsDouble <= int.MaxValue) {
            maxPermutations = (int) maxPermutationsDouble;
            useMaxPermutations = true;
        } else {
            maxPermutations = 0;
            useMaxPermutations = false;
        }

        bool brokeFromLoopMax = false;

        int i = 0;
        while (true) {
            i++;

            var input = new InputPermutation();
            int frameCounter = 0;
            var prevAction = InputAction.None;

            while (frameCounter < cfg.Frames) {
                int frames = random.Next(1, cfg.Frames - frameCounter + 1);
                frameCounter += frames;
                var selectedAction = actions[random.Next(actions.Length)];

                if (selectedAction == prevAction) {
                    var lastInput = input[^1];
                    lastInput.Frames += frames;
                    input[^1] = lastInput;
                } else {
                    input.Add((frames, selectedAction));
                    prevAction = selectedAction;
                }
            }

            inputPermutations.Add(input);

            if (i == updateInterval) {
                i = 0;
                int elapsedTime = (int) stopwatch.Elapsed.TotalMilliseconds;
                await Application.Instance.InvokeAsync(() => {
                    individualProgressBar.Value = elapsedTime;
                    UpdateLayout();
                });

                if (!isRunning) {
                    return [];  // canceled
                }

                if (elapsedTime >= cfg.InputGenerationTime) {
                    break;
                }

                if (useMaxPermutations && inputPermutations.Count >= maxPermutations) {
                    brokeFromLoopMax = true;
                    break;
                }
            }
        }

        if (brokeFromLoopMax) {
            await Log("Exiting generation early due to reaching max possible permutations");
        }

        return inputPermutations;
    }

    private class InputPermutationComparer : IEqualityComparer<InputPermutation> {
        public bool Equals(InputPermutation? x, InputPermutation? y) {
            if (x!.Count != y!.Count) {
                return false;
            }

            for (int i = 0; i < x.Count; i++) {
                if (x[i].Frames != y[i].Frames || x[i].Action != y[i].Action) {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(InputPermutation obj) {
            var hash = new HashCode();
            foreach (var item in obj) {
                hash.Add(item.Frames);
                hash.Add(item.Action);
            }
            return hash.ToHashCode();
        }
    }

    private InputAction GenerateAvailableActions() {
        var actions = (InputAction) 0;

        if (cfg.Axis == Axis.X) {
            actions |= InputAction.Left;
            actions |= InputAction.Right;

            if (initialState.OnGround) {
                actions |= InputAction.Down;
            }
        } else {
            actions |= InputAction.Jump;
            actions |= InputAction.Down;
        }

        switch (cfg.DisabledKey) {
            case DisabledKey.L:
                actions &= ~InputAction.Left;
                break;
            case DisabledKey.R:
                actions &= ~InputAction.Right;
                break;
            case DisabledKey.J:
                actions &= ~InputAction.Jump;
                break;
            case DisabledKey.D:
                actions &= ~InputAction.Down;
                break;
        }

        return actions;
    }

    private static string FormatInputPermutationText((float position, float speed, InputPermutation inputs) inputPermutation) {
        int speedPrecisionOffset = inputPermutation.speed switch {
            < -100 => 3,
            < -10 => 2,
            > 100 => 2,
            > 10 => 1,
            < 0 => 1,
            _ => 0
        };

        string position = inputPermutation.position.ToString("F10");
        string speed = inputPermutation.speed.ToString($"F{8 - speedPrecisionOffset}");

        var builder = new StringBuilder($"({position}, {speed}) ");
        foreach (var input in inputPermutation.inputs) {
            if (input.Action == InputAction.None) {
                builder.Append($"{input.Frames} ");
            } else {
                builder.Append($"{input.Frames},{CharForAction(input.Action)} ");
            }
        }
        return builder.ToString();
    }

    private static string FormatInputPermutationKey(InputPermutation inputs) {
        var builder = new StringBuilder();
        foreach (var input in inputs) {
            if (input.Action == InputAction.None) {
                builder.Append($"{input.Frames}\n");
            } else {
                builder.Append($"{input.Frames},{CharForAction(input.Action)}\n");
            }
        }
        return builder.ToString();
    }

    #endregion
    #region Simulation

    private const float RunAccel = 1000.0f;
    private const float RunReduce = 400.0f;
    private const float AirMult = 0.65f;
    private const float MaxRun = 90.0f;
    private const float HoldingMaxRun = 70.0f;
    private const float DuckFriction = 500.0f;

    private (float position, float speed) SimX(InputPermutation inputs) {
        float x = initialState.Position;
        float speedX = initialState.Speed;
        bool grounded = initialState.OnGround;
        float multAccel = grounded ? initialState.DeltaTime * RunAccel : AirMult * initialState.DeltaTime * RunAccel;
        float multReduce = grounded ? initialState.DeltaTime * RunReduce : AirMult * initialState.DeltaTime * RunReduce;
        float max = initialState.Holding ? HoldingMaxRun : MaxRun;

        foreach (var inputLine in inputs) {
            foreach (var inputAction in Enumerable.Repeat(inputLine.Action, inputLine.Frames)) {
                // celeste code (from Player.NormalUpdate) somewhat loosely simplified

                if (grounded && inputAction == InputAction.Down) {
                    speedX = Approach(speedX, 0.0f, DuckFriction * initialState.DeltaTime);
                } else {
                    // get input first
                    int moveX = inputAction switch {
                        InputAction.Left => -1,
                        InputAction.Right => 1,
                        _ => 0,
                    };

                    if (Math.Abs(speedX) <= max || Math.Sign(speedX) != moveX) {
                        speedX = Approach(speedX, max * moveX, multAccel);
                    } else {
                        speedX = Approach(speedX, max * moveX, multReduce);
                    }
                }

                // calculate position third
                x += speedX * initialState.DeltaTime;
            }
        }

        return ((float) Math.Round(x, 10), (float) Math.Round(speedX, 8));
    }

    private const float MaxFall = 160.0f;
    private const float FastMaxFall = 240.0f;
    private const float FastMaxAccel = 300.0f;
    private const float Gravity = 900.0f;
    private const float HalfGravThreshold = 40.0f;

    private (float position, float speed) SimY(InputPermutation inputs) {
        float y = initialState.Position;
        float speedY = initialState.Speed;
        float maxFall = initialState.MaxFallSpeed;
        int jumpTimer = initialState.JumpTimer;

        foreach (var inputLine in inputs) {
            foreach (var inputAction in Enumerable.Repeat(inputLine.Action, inputLine.Frames)) {
                // celeste code (from Player.NormalUpdate) somewhat loosely simplified

                // calculate speed
                if (inputAction == InputAction.Down && speedY >= MaxFall) {
                    maxFall = Approach(maxFall, FastMaxFall, FastMaxAccel * initialState.DeltaTime);
                } else {
                    maxFall = Approach(maxFall, MaxFall, FastMaxAccel * initialState.DeltaTime);
                }

                float mult;
                if (Math.Abs(speedY) <= HalfGravThreshold && (inputAction == InputAction.Jump || initialState.AutoJump)) {
                    mult = Gravity * 0.5f * initialState.DeltaTime;
                } else {
                    mult = Gravity * initialState.DeltaTime;
                }

                speedY = Approach(speedY, maxFall, mult);

                if (jumpTimer > 0) {
                    if (inputAction == InputAction.Jump || initialState.AutoJump) {
                        speedY = Math.Min(speedY, initialState.Speed);
                    } else {
                        jumpTimer = 0;
                    }
                }

                jumpTimer--;

                // calculate position
                y += speedY * initialState.DeltaTime;
            }
        }

        return ((float) Math.Round(y, 10), (float) Math.Round(speedY, 8));
    }


    // directly from Monocle.Calc
    private static float Approach(float val, float target, float maxMove) =>
        val <= target ? Math.Min(val + maxMove, target) : Math.Max(val - maxMove, target);

    #endregion

    private void CopySelectedOutput(object? sender, EventArgs e) {
        if (outputsList.Items.Count == 0) {
            return;
        }

        string selectedItemKey = outputsList.Items[outputsList.SelectedIndex].Key!;
        if (selectedItemKey == InvalidOutput) {
            return;
        }

        var builder = new StringBuilder();

        bool anyInvalid = false;
        foreach (string inputLine in selectedItemKey.TrimEnd().Split('\n')) {
            if (ActionLine.TryParse(inputLine + additionalInputsText.Text, out var actionLine)) {
                builder.AppendLine(actionLine.ToString());
            } else {
                builder.AppendLine($"# Invalid input '{inputLine + additionalInputsText.Text}'");
                anyInvalid = true;
            }
        }
        if (anyInvalid) {
            builder.AppendLine($"# NOTE: Your additional inputs ('{additionalInputsText.Text}') are most likely not correctly formatted");
        }

        Clipboard.Instance.Clear();
        Clipboard.Instance.Text = builder.ToString();
    }

    private void FetchInitialState() {
        if (!CommunicationWrapper.Connected) {
            return;
        }

        var gameStateResult = CommunicationWrapper.GetGameState().Result;
        if (gameStateResult == null) {
            Console.Error.WriteLine("Failed to get game state");
            MessageBox.Show("Failed to get game state, please try again", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }

        gotInitialState = true;
        var gameState = gameStateResult.Value;

        initialState = new InitialState {
            DeltaTime = gameState.DeltaTime,
            Positions = (gameState.Player.Position.X + gameState.Player.PositionRemainder.X, gameState.Player.Position.Y + gameState.Player.PositionRemainder.Y),
            Speeds = (gameState.Player.Speed.X, gameState.Player.Speed.Y),
            OnGround = gameState.Player is { OnGround: true, Speed.Y: >= 0f },
            Holding = gameState.Player.IsHolding,
            JumpTimer = (int) MathF.Floor(gameState.Player.JumpTimer / gameState.DeltaTime),
            AutoJump = gameState.Player.AutoJump,
            MaxFallSpeed = gameState.Player.MaxFall,
            ChapterTime = gameState.ChapterTime,
            RoomName = gameState.RoomName,
            PlayerStateName = gameState.PlayerStateName
        };

        initialStateText.Text = initialState.ToString();
    }

    public struct InitialState {
        public float DeltaTime;

        public (float X, float Y) Positions;
        public (float X, float Y) Speeds;
        // X axis:
        public bool OnGround;
        public bool Holding;
        // Y axis:
        public int JumpTimer;
        public bool AutoJump;
        public float MaxFallSpeed;
        // finalized:
        public float Position;
        public float Speed;
        // display/logic only:
        public string ChapterTime;
        public string RoomName;
        public string PlayerStateName;

        public override string ToString() {
            return $"""
                    Position: {Positions.X}, {Positions.Y}
                    Speed: {Speeds.X}, {Speeds.Y}
                    Grounded: {OnGround}
                    Holding: {Holding}
                    Jump Timer: {JumpTimer}f
                    Auto Jump: {AutoJump}
                    Max Fall: {MaxFallSpeed}
                    State: {PlayerStateName}
                    [{RoomName}] Timer: {ChapterTime}
                    """;
        }
    }

    public record Config {
        public bool WindowFirstOpen = true;
        public int Frames;
        public Axis Axis;
        public (float Min, float Max) PositionFilter;
        public float GoalSpeed;
        public DisabledKey DisabledKey;
        public OutputSortingPriority OutputSortingPriority;
        public int RNGThresholdSlow;
        public int RNGThreshold;
        public bool HideDuplicateInputs;
        public int InputGenerationTime;  // in ms
        public string AdditionalInputs = string.Empty;
        public InitialState InitialState;
        public string InitialStateInfo = string.Empty;
    }

    public enum Axis { X, Y }
    public enum DisabledKey { None, Auto, L, R, J, D }
    public enum OutputSortingPriority { Position, Speed }
}
