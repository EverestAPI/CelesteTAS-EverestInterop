using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Tedd.RandomUtils;

namespace CelesteStudio.Tool;

public sealed class RadelineSimForm : Form {
    private const string Version = "0.0.1";

    private readonly TextArea initialStateControl;
    private readonly TextArea appendKeysControl;
    private readonly TextArea logControl;
    private readonly Button getInitialStateControl;
    private readonly ProgressBar progressBarControl;
    private readonly DropDown outputSortingControl;
    private readonly DropDown axisControl;
    private readonly DropDown disabledKeyControl;
    private readonly NumericStepper framesControl;
    private readonly NumericStepper inputGenerationTimeControl;
    private readonly NumericStepper positionFilterMinControl;
    private readonly NumericStepper positionFilterMaxControl;
    private readonly NumericStepper goalSpeedControl;
    private readonly NumericStepper rngThresholdSlowControl;
    private readonly NumericStepper rngThresholdControl;
    private readonly CheckBox hideDuplicatesControl;
    private readonly ListBox outputsControl;

    private static InitialState initialState;
    private static RadelineSimConfig cfg;
    private string[] generatorKeys = [];
    private bool gotInitialState;

    public RadelineSimForm(RadelineSimConfig formPersistence) {
        cfg = formPersistence;

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

        initialStateControl = new TextArea { ReadOnly = true, Wrap = true, Font = FontManager.StatusFont, Width = 180, Height = 190};
        getInitialStateControl = new Button((_, _) => GetInitialState()) { Text = "Get Initial State", Width = 150 };
        logControl = new TextArea { ReadOnly = true, Wrap = true, Font = FontManager.StatusFont, Width = rowWidth };
        outputSortingControl = new DropDown {
            Items = {
                new ListItem { Text = "Position" },
                new ListItem { Text = "Speed" }
            },
            SelectedKey = "Position",
            Width = rowWidth
        };
        axisControl = new DropDown {
            Items = {
                new ListItem { Text = "X (horizontal)", Key = "X" },
                new ListItem { Text = "Y (vertical)", Key = "Y" }
            },
            SelectedKey = "X",
            Width = rowWidth
        };
        disabledKeyControl = new DropDown {
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
        framesControl = new NumericStepper { Value = 10, MinValue = 1, MaxValue = 200, Width = rowWidth };
        inputGenerationTimeControl = new NumericStepper { Value = 5, MinValue = 1, MaxValue = 200, Width = rowWidth };
        positionFilterMinControl = new NumericStepper { DecimalPlaces = CommunicationWrapper.GameSettings.PositionDecimals, Width = rowWidth };
        positionFilterMaxControl = new NumericStepper { DecimalPlaces = CommunicationWrapper.GameSettings.PositionDecimals, Width = rowWidth };
        goalSpeedControl = new NumericStepper { DecimalPlaces = CommunicationWrapper.GameSettings.PositionDecimals, Width = rowWidth };
        rngThresholdControl = new NumericStepper { Value = 23, MinValue = 1, MaxValue = 200, Width = rowWidth };
        rngThresholdSlowControl = new NumericStepper { Value = 15, MinValue = 1, MaxValue = 200, Width = rowWidth };
        appendKeysControl = new TextArea { Font = FontManager.EditorFont, Width = rowWidth, Height = 22};
        hideDuplicatesControl = new CheckBox { Width = rowWidth, Checked = true };
        outputsControl = new ListBox { Font = FontManager.StatusFont, Width = 600, Height = 500 };
        progressBarControl = new ProgressBar { Width = 300 };

        outputsControl.SelectedIndexChanged += OutputsOnSelectedIndexChanged;

        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 8) };
        layout.BeginHorizontal();

        layout.BeginVertical();
        layout.BeginHorizontal();
        layout.Add(initialStateControl);
        layout.Add(logControl);
        layout.EndBeginHorizontal();
        layout.Add(getInitialStateControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Frames", ToolTip = "Number of frames to simulate" });
        layout.Add(framesControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Axis" });
        layout.Add(axisControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Position Filter (Min)", ToolTip = positionFilterTooltip });
        layout.Add(positionFilterMinControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Position Filter (Max)", ToolTip = positionFilterTooltip });
        layout.Add(positionFilterMaxControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Goal Speed", ToolTip = "This is calculated with |final speed - goal speed|" });
        layout.Add(goalSpeedControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Disabled Key", ToolTip = "Disable generating a certain key. Auto will disable keys that can't ever affect input" });
        layout.Add(disabledKeyControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Output Sorting Priority" });
        layout.Add(outputSortingControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "RNG Threshold (All Keys)", ToolTip = "Frame count at which to start using the RNG input generation method " +
                                                                                    "(instead of sequential) when all keys are enabled"});
        layout.Add(rngThresholdSlowControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "RNG Threshold (Disabled Key)", ToolTip = "Frame count at which to start using the RNG input generation method " +
                                                                                        "(instead of sequential) when a key is disabled" });
        layout.Add(rngThresholdControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Input Generation Time (RNG)", ToolTip = "How long to spend generating random inputs, in seconds"});
        layout.Add(inputGenerationTimeControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Appended Keys", ToolTip = "Keys the formatter adds, e.g. \"jg\" to hold jump and grab as well"});
        layout.Add(appendKeysControl);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Hide Duplicate Inputs", ToolTip = "Don't output multiple inputs with the same resulting position/speed (disable for performance)"});
        layout.Add(hideDuplicatesControl);
        layout.EndBeginHorizontal();
        layout.EndHorizontal();
        layout.EndVertical();

        layout.Add(outputsControl);

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
                        (new Button((_, _) => Run()) { Text = "Run", Width = 150 }),
                        progressBarControl
                    }
                }
            }
        };

        Resizable = false;
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Closed += (_, _) => SetupSimConfig();
        FormFromPersistence();
    }

    private void FormFromPersistence() {
        if (cfg.WindowFirstOpen) {
            cfg.WindowFirstOpen = false;
            return;
        }

        framesControl.Value = cfg.Frames;
        axisControl.SelectedKey = cfg.Axis.ToString();
        positionFilterMinControl.Value = cfg.PositionFilter.Min;
        positionFilterMaxControl.Value = cfg.PositionFilter.Max;
        goalSpeedControl.Value = cfg.GoalSpeed;
        disabledKeyControl.SelectedKey = cfg.DisabledKey.ToString();
        outputSortingControl.SelectedKey = cfg.OutputSortingPriority.ToString();
        rngThresholdControl.Value = cfg.RNGThreshold;
        rngThresholdSlowControl.Value = cfg.RNGThresholdSlow;
        inputGenerationTimeControl.Value = cfg.InputGenerationTime / 1000;
        appendKeysControl.Text = cfg.AppendKeys;
        hideDuplicatesControl.Checked = cfg.HideDuplicateInputs;
        initialStateControl.Text = cfg.InitialStateInfo;

        initialState = cfg.InitialState;
        gotInitialState = true;
    }

    private void Log(string message) {
        logControl.Text += $"- {message}\n";
        logControl.ScrollToEnd();
    }

    private void Run() {
        if (!gotInitialState) {
            Log("No initial state");
            return;
        }

        SetupSimConfig();
        ICollection<List<(int frames, string key)>> inputPermutations;
        progressBarControl.Value = 0;

        if (cfg.DisabledKey == DisabledKey.Auto) {
            cfg.DisabledKey = DisabledKey.None;

            // do some math to determine if a key can ever affect movement
            if (cfg.Axis == Axis.X) {
                // disable holding backwards if speed can't ever drop below zero due to friction
                bool speedHighEnoughNotGrounded = !initialState.OnGround && Math.Abs(initialState.Speed) > cfg.Frames * (65f / 6f);
                bool speedHighEnoughGrounded = initialState.OnGround && Math.Abs(initialState.Speed) > cfg.Frames * (50f / 3f);

                if (speedHighEnoughNotGrounded || speedHighEnoughGrounded)
                    cfg.DisabledKey = initialState.Speed > 0f ? DisabledKey.L : DisabledKey.R;
            } else {
                // disable jump if past jump peak, or down if can't ever reach fast fall speed
                if (initialState.Speed > 40f || initialState.AutoJump)
                    cfg.DisabledKey = DisabledKey.J;
                else if (initialState.Speed + cfg.Frames * 15f <= 160)
                    cfg.DisabledKey = DisabledKey.D;
            }
        } else {
            bool cantDisableX = cfg.Axis == Axis.X && cfg.DisabledKey == DisabledKey.J;
            bool cantDisableY = cfg.Axis == Axis.Y && new[] {DisabledKey.L, DisabledKey.R}.Contains(cfg.DisabledKey);

            if (cantDisableX || cantDisableY) {
                Log($"Didn't disable {cfg.DisabledKey} key since it wouldn't have been generated anyway");
                cfg.DisabledKey = DisabledKey.None;
            }
        }

        if (cfg.DisabledKey != DisabledKey.None)
            Log($"Disabled generating {cfg.DisabledKey} inputs");

        int RNGThreshold = (generatorKeys = GeneratorKeys()).Length switch {
            3 => cfg.RNGThresholdSlow,
            4 => cfg.RNGThresholdSlow - 2,
            _ => cfg.RNGThreshold
        };

        if (cfg.Frames < RNGThreshold) {
            Log("Building permutations using sequential method…");
            inputPermutations = BuildInputPermutationsSequential();
        }
        else {
            Log("Building permutations using RNG method…");
            inputPermutations = BuildInputPermutationsRng();
        }

        Log($"Generated permutations: {inputPermutations.Count}");
        UpdateLayout();

        // store as position and speed dicts, for performance
        var filteredPermutations = new SortedDictionary<float, Dictionary<float, List<List<(int frames, string key)>>>>();
        var speeds = new HashSet<float>();
        Func<List<(int frames, string key)>, (float position, float speed)> simFunction = cfg.Axis == Axis.X ? SimX : SimY;
        progressBarControl.MaxValue = inputPermutations.Count;
        Log("Simulating inputs…");
        int i = 0;

        foreach (var permutation in inputPermutations) {
            var simResult = simFunction(permutation);
            i++;

            if (i % 1000 == 0) {
                progressBarControl.Value = i;
                UpdateLayout();
            }

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
        }

        inputPermutations = [];
        List<(float position, float speed, List<(int frames, string key)> inputs)> outputPermutations = [];

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
            foreach (var speed in speeds.ToList().OrderByDescending(s => Math.Abs(s - cfg.GoalSpeed))) {
                foreach (var position in filteredPermutations.Keys) {
                    if (filteredPermutations[position].TryGetValue(speed, out var permutations)) {
                        foreach (var permutation in permutations) {
                            outputPermutations.Add((position, speed, permutation));
                        }
                    }
                }
            }
        }

        Log($"Filtered permutations: {outputPermutations.Count}");
        outputsControl.Items.Clear();
        UpdateLayout();
        filteredPermutations = [];
        i = 0;

        // insert results into output window
        foreach (var inputPermutation in outputPermutations) {
            outputsControl.Items.Add(new ListItem { Text = FormatInputPermutation(inputPermutation), Key = FormatInputPermutationCompact(inputPermutation.inputs) });
            i++;

            if (i == 1000) {
                UpdateLayout();
                i = 0;
            }
        }

        outputPermutations = [];
    }

    private ICollection<List<(int frames, string key)>> BuildInputPermutationsSequential() {
        int expectedPermutations = (int)Math.Pow(generatorKeys.Length, cfg.Frames);
        List<List<(int frames, string key)>> inputPermutations = new(expectedPermutations);
        progressBarControl.MaxValue = expectedPermutations;
        int lastReportedProgress = 0;
        int i = 0;

        foreach (var permutation in CartesianProduct(generatorKeys, cfg.Frames)) {
            i++;
            List<(int frames, string key)> permutationFormatted = [];
            string? currentInput = null;
            int inputLen = 0;;

            // convert messy inputs to the compact format
            foreach (var key in permutation) {
                if (currentInput == null)
                    currentInput = key;

                if (key == currentInput)
                    inputLen++;
                else {
                    permutationFormatted.Add((inputLen, currentInput));
                    currentInput = key;
                    inputLen = 1;
                }
            }

            permutationFormatted.Add((inputLen, currentInput)!);
            inputPermutations.Add(permutationFormatted);

            if (i - lastReportedProgress > 10000) {
                progressBarControl.Value = i;
                lastReportedProgress = i;
                UpdateLayout();
            }
        }

        return inputPermutations;
    }

    private HashSet<List<(int frames, string key)>> BuildInputPermutationsRng() {
        var inputPermutations = new HashSet<List<(int frames, string key)>>(new ListTupleComparer());
        int keysLen = generatorKeys.Length;
        double maxPermutationsDouble = Math.Pow(generatorKeys.Length, cfg.Frames);
        int maxPermutations = 0;
        bool useMaxPermutations;
        bool brokeFromLoopMax = false;
        int updateInterval = 1000000 / cfg.Frames;
        var random = new FastRandom();
        progressBarControl.MaxValue = cfg.InputGenerationTime;
        var stopwatch = Stopwatch.StartNew();
        int i = 0;

        if (maxPermutationsDouble <= int.MaxValue) {
            maxPermutations = (int) maxPermutationsDouble;
            useMaxPermutations = true;
        } else
            useMaxPermutations = false;

        while (true) {
            i++;
            var inputs = new List<(int frames, string key)>();
            int frameCounter = 0;
            string prevKey = "-";

            while (frameCounter < cfg.Frames) {
                int frames = random.Next(1, cfg.Frames - frameCounter + 1);
                frameCounter += frames;
                string selectedKey = generatorKeys[random.Next(keysLen)];

                if (selectedKey == prevKey) {
                    var lastInput = inputs.Last();
                    lastInput.frames += frames;
                } else {
                    inputs.Add((frames, selectedKey));
                    prevKey = selectedKey;
                }
            }

            inputPermutations.Add(inputs);

            if (i == updateInterval) {
                i = 0;
                int elapsedTime = (int) stopwatch.Elapsed.TotalMilliseconds;
                progressBarControl.Value = elapsedTime;
                UpdateLayout();

                if (elapsedTime >= cfg.InputGenerationTime)
                    break;

                if (useMaxPermutations && inputPermutations.Count >= maxPermutations) {
                    brokeFromLoopMax = true;
                    break;
                }
            }
        }

        if (brokeFromLoopMax)
            Log($"Exiting generation early due to reaching max possible permutations ({maxPermutations})");

        return inputPermutations;
    }

    private static (float position, float speed) SimX(List<(int frames, string key)> inputs) {
        float x = initialState.Position;
        float speedX = initialState.Speed;
        bool grounded = initialState.OnGround;
        float mult1 = grounded ? 0.0166667f * 1000f : 0.65f * 0.0166667f * 1000f;
        float mult2 = grounded ? 0.0166667f * 400f : 0.65f * 0.0166667f * 400f;
        float max = initialState.Holding ? 70f : 90f;

        foreach (var inputLine in inputs) {
            foreach (string inputKey in Enumerable.Repeat(inputLine.key, inputLine.frames)) {
                // celeste code (from Player.NormalUpdate) somewhat loosely simplified

                if (grounded && inputKey.Equals("d"))
                    speedX = Approach(speedX, 0.0f, 500f * 0.0166667f);
                else {
                    // get input first
                    float moveX = inputKey switch {
                        "l" => -1f,
                        "r" => 1f,
                        _ => 0f
                    };

                    if (Math.Abs(speedX) <= max || (speedX == 0.0f ? 0.0f : (float) Math.CopySign(1, speedX)) != moveX)
                        speedX = Approach(speedX, max * moveX, mult1);
                    else
                        speedX = Approach(speedX, max * moveX, mult2);
                }

                // calculate position third
                x += speedX * 0.0166667f;
            }
        }

        return ((float) Math.Round(x, 10), (float) Math.Round(speedX, 8));
    }

    private static (float position, float speed) SimY(List<(int frames, string key)> inputs) {
        float y = initialState.Position;
        float speedY = initialState.Speed;
        float maxFall = initialState.MaxFall;
        int jumpTimer = initialState.JumpTimer;
        float mult;

        foreach (var inputLine in inputs) {
            foreach (string inputKey in Enumerable.Repeat(inputLine.key, inputLine.frames)) {
                // celeste code (from Player.NormalUpdate) somewhat loosely simplified

                // get input first
                char moveY = inputKey switch {
                    "j" => 'j',
                    "d" => 'd',
                    _ => '_'
                };

                // calculate speed second
                if (moveY == 'd' && speedY >= 160f)
                    maxFall = Approach(maxFall, 240f, 300f * 0.0166667f);
                else
                    maxFall = Approach(maxFall, 160f, 300f * 0.0166667f);

                if (Math.Abs(speedY) <= 40f && (moveY == 'j' || initialState.AutoJump))
                    mult = 900f * 0.5f * 0.0166667f;
                else
                    mult = 900f * 0.0166667f;

                speedY = Approach(speedY, maxFall, mult);

                if (jumpTimer > 0) {
                    if (moveY == 'j' || initialState.AutoJump)
                        speedY = Math.Min(speedY, initialState.Speed);
                    else
                        jumpTimer = 0;
                }

                jumpTimer--;

                // calculate position third
                y += speedY * 0.0166667f;
            }
        }

        return ((float) Math.Round(y, 10), (float) Math.Round(speedY, 8));
    }

    private static IEnumerable<IEnumerable<string>> CartesianProduct(string[] source, int repeat) {
        IEnumerable<IEnumerable<string>> seed = [[]];

        for (int i = 0; i < repeat; i++) {
            seed = from seq in seed
                from item in source
                select seq.Concat([item]);
        }

        return seed;
    }

    private class ListTupleComparer : IEqualityComparer<List<(int frames, string key)>> {
        public bool Equals(List<(int frames, string key)>? x, List<(int frames, string key)>? y) {
            if (x!.Count != y!.Count)
                return false;

            for (int i = 0; i < x.Count; i++) {
                if (x[i].frames != y[i].frames || x[i].key != y[i].key)
                    return false;
            }

            return true;
        }

        public int GetHashCode(List<(int frames, string key)> obj) {
            unchecked {
                int hash = 17;

                foreach (var item in obj) {
                    hash = hash * 31 + item.frames.GetHashCode();
                    hash = hash * 31 + (item.key?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }
    }

    // directly from Monocle.Calc
    private static float Approach(float val, float target, float maxMove) =>
        val <= target ? Math.Min(val + maxMove, target) : Math.Max(val - maxMove, target);

    private static string[] GeneratorKeys() {
        var keys = new List<string>();

        if (cfg.Axis == Axis.X) {
            keys.AddRange(["", "l", "r"]);

            if (initialState.OnGround)
                keys.Add("d");
        } else
            keys.AddRange(["", "j", "d"]);

        if (cfg.DisabledKey != DisabledKey.None)
            keys.Remove(cfg.DisabledKey.ToString().ToLower());

        return keys.ToArray();
    }

    private static string FormatInputPermutation((float position, float speed, List<(int frames, string key)> inputs) inputPermutation) {
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
        var inputsDisplay = new StringBuilder($"({position}, {speed}) ");

        foreach (var input in inputPermutation.inputs) {
            string comma = string.IsNullOrEmpty(input.key) ? "" : ",";
            inputsDisplay.Append($"{input.frames}{comma}{input.key.ToUpper()} ");
        }

        return inputsDisplay.ToString();
    }

    private static string FormatInputPermutationCompact(List<(int frames, string key)> inputs) {
        var inputsCompact = new StringBuilder();

        foreach (var input in inputs) {
            inputsCompact.Append($"{input.frames},{input.key}\n");
        }

        return inputsCompact.ToString();
    }

    private void OutputsOnSelectedIndexChanged(object? sender, EventArgs e) {
        if (!outputsControl.Items.Any()) { return; }
        var appendKeys = appendKeysControl.Text.Where(c => !char.IsWhiteSpace(c));
        string selectedItemKey = outputsControl.Items[outputsControl.SelectedIndex].Key!;
        string[] inputLines = selectedItemKey.TrimEnd().Split('\n');
        var inputsProcessed = new StringBuilder();

        foreach (string inputLine in inputLines) {
            inputsProcessed.Append(inputLine);

            foreach (char appendKey in appendKeys) {
                inputsProcessed.Append($",{appendKey}");
            }

            inputsProcessed.Append('\n');
        }

        Clipboard.Instance.Clear();
        Clipboard.Instance.Text = inputsProcessed.ToString();
    }

    private void SetupSimConfig() {
        float positionFilterMinValue = (float) positionFilterMinControl.Value;
        float positionFilterMaxValue = (float) positionFilterMaxControl.Value;

        // fix for accidentally backwards order
        if (positionFilterMinValue > positionFilterMaxValue)
            (positionFilterMinValue, positionFilterMaxValue) = (positionFilterMaxValue, positionFilterMinValue);

        Enum.TryParse(axisControl.SelectedKey, out Axis axisSelected);
        Enum.TryParse(disabledKeyControl.SelectedKey, out DisabledKey disabledKeySelected);
        Enum.TryParse(outputSortingControl.SelectedKey, out OutputSortingPriority outputSortingPrioritySelected);

        cfg.Frames = (int) framesControl.Value;
        cfg.Axis = axisSelected;
        cfg.PositionFilter = (positionFilterMinValue, positionFilterMaxValue);
        cfg.GoalSpeed = (float) goalSpeedControl.Value;
        cfg.DisabledKey = disabledKeySelected;
        cfg.OutputSortingPriority = outputSortingPrioritySelected;
        cfg.RNGThresholdSlow = (int) rngThresholdSlowControl.Value;
        cfg.RNGThreshold = (int) rngThresholdControl.Value;
        cfg.HideDuplicateInputs = hideDuplicatesControl.Checked!.Value;
        cfg.InputGenerationTime = (int) inputGenerationTimeControl.Value * 1000;
        cfg.AppendKeys = appendKeysControl.Text;
        cfg.InitialState = initialState;
        cfg.InitialStateInfo = initialStateControl.Text;

        initialState.Position = axisSelected == Axis.X ? initialState.Positions.X : initialState.Positions.Y;
        initialState.Speed = axisSelected == Axis.X ? initialState.Speeds.X : initialState.Speeds.Y;
    }

    private void GetInitialState() {
        if (!CommunicationWrapper.Connected) {
            Log("Not connected to game");
            return;
        }

        var gameStateResult = CommunicationWrapper.GetGameState().Result;

        if (gameStateResult == null) {
            Console.Error.WriteLine("Failed to get game state");
            Log("Failed to get game state, please try again");
            return;
        }

        gotInitialState = true;
        var gameState = gameStateResult.Value;

        initialState = new InitialState {
            Positions = (gameState.Player.Position.X + gameState.Player.PositionRemainder.X, gameState.Player.Position.Y + gameState.Player.PositionRemainder.Y),
            Speeds = (gameState.Player.Speed.X, gameState.Player.Speed.Y),
            OnGround = gameState.Player is { OnGround: true, Speed.Y: >= 0f },
            Holding = gameState.Player.IsHolding,
            JumpTimer = gameState.Player.JumpTimer,
            AutoJump = gameState.Player.AutoJump,
            MaxFall = gameState.Player.MaxFall
        };

        initialStateControl.Text = CommunicationWrapper.GameInfo;
    }

    public struct InitialState {
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

    public enum Axis { X, Y }
    public enum DisabledKey { None, Auto, L, R, J, D }
    public enum OutputSortingPriority { Position, Speed }
}

public class RadelineSimConfig {
    public bool WindowFirstOpen = true;
    public int Frames;
    public RadelineSimForm.Axis Axis;
    public (float Min, float Max) PositionFilter;
    public float GoalSpeed;
    public RadelineSimForm.DisabledKey DisabledKey;
    public RadelineSimForm.OutputSortingPriority OutputSortingPriority;
    public int RNGThresholdSlow;
    public int RNGThreshold;
    public bool HideDuplicateInputs;
    public int InputGenerationTime;  // in ms
    public string AppendKeys;
    public RadelineSimForm.InitialState InitialState;
    public string InitialStateInfo;
}
