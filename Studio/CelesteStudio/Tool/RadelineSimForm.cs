using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tedd.RandomUtils;

namespace CelesteStudio.Tool;

public sealed class RadelineSimForm : Form {
    private const string Version = "1.0.0";

    private readonly TextArea initialStateControl;
    private readonly TextArea appendKeysControl;
    private readonly Button runButton;
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

    private const string InvalidOutput = "InvalidOutput";
    private readonly ListBox outputsControl;

    private Eto.Forms.Dialog? progressPopup;
    private TextArea logControl = null!;
    private ProgressBar individualProgressBar = null!;

    private static InitialState initialState;
    private static RadelineSimConfig cfg;
    private char[] generatorKeys = [];
    private bool gotInitialState;
    private bool isRunning;
    private readonly List<Control> disableControls;

    public RadelineSimForm(RadelineSimConfig formPersistence) {
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

        runButton = new Button((_, _) => Run()) { Text = "Run", Width = 150 };
        if (!gotInitialState) {
            runButton.Enabled = false;
            runButton.ToolTip = "No initial state";
        } else if (initialState.PlayerStateName != "StNormal") {
            runButton.Enabled = false;
            runButton.ToolTip = $"Player state must be StNormal, not {initialState.PlayerStateName}";
        }

        initialStateControl = new TextArea { ReadOnly = true, Wrap = true, Font = FontManager.StatusFont, Width = 180, Height = 190};
        var getInitialStateButton = new Button((_, _) => {
            GetInitialState();

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
        positionFilterMinControl = new NumericStepper { DecimalPlaces = 2, Width = rowWidth };
        positionFilterMaxControl = new NumericStepper { DecimalPlaces = 2, Width = rowWidth };
        goalSpeedControl = new NumericStepper { DecimalPlaces = 2, Width = rowWidth };
        rngThresholdControl = new NumericStepper { Value = 23, MinValue = 1, MaxValue = 200, Width = rowWidth };
        rngThresholdSlowControl = new NumericStepper { Value = 15, MinValue = 1, MaxValue = 200, Width = rowWidth };
        appendKeysControl = new TextArea { Font = FontManager.EditorFont, Width = rowWidth, Height = 22};
        hideDuplicatesControl = new CheckBox { Width = rowWidth, Checked = true };
        outputsControl = new ListBox { Font = FontManager.StatusFont, Width = 600, Height = 500 };
        outputsControl.SelectedIndexChanged += OutputsOnSelectedIndexChanged;

        const string positionFilterTooltip = "Only show results within this position range (min and max can be backwards, won't make a difference)";

        var configLayout = new DynamicLayout { DefaultSpacing = new Size(10, 8) };

        configLayout.BeginVertical();
        configLayout.BeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Frames", ToolTip = "Number of frames to simulate" });
        configLayout.Add(framesControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Axis" });
        configLayout.Add(axisControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Position Filter (Min)", ToolTip = positionFilterTooltip });
        configLayout.Add(positionFilterMinControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Position Filter (Max)", ToolTip = positionFilterTooltip });
        configLayout.Add(positionFilterMaxControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Goal Speed", ToolTip = "This is calculated with |final speed - goal speed|" });
        configLayout.Add(goalSpeedControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Disabled Key", ToolTip = "Disable generating a certain key. Auto will disable keys that can't ever affect input" });
        configLayout.Add(disabledKeyControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "RNG Threshold (All Keys)", ToolTip = "Frame count at which to start using the RNG input generation method (instead of sequential) when all keys are enabled"});
        configLayout.Add(rngThresholdSlowControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "RNG Threshold (Disabled Key)", ToolTip = "Frame count at which to start using the RNG input generation method (instead of sequential) when a key is disabled" });
        configLayout.Add(rngThresholdControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Input Generation Time (RNG)", ToolTip = "How long to spend generating random inputs, in seconds"});
        configLayout.Add(inputGenerationTimeControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Output Sorting Priority" });
        configLayout.Add(outputSortingControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Appended Keys", ToolTip = "Keys added when copying, e.g. \"jg\" to hold jump and grab as well"});
        configLayout.Add(appendKeysControl);
        configLayout.EndBeginHorizontal();
        configLayout.AddCentered(new Label { Text = "Hide Duplicate Inputs", ToolTip = "Don't output multiple inputs with the same resulting position/speed (disable for performance)"});
        configLayout.Add(hideDuplicatesControl);
        configLayout.EndBeginHorizontal();
        configLayout.EndHorizontal();
        configLayout.EndVertical();

        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 8) };
        layout.BeginHorizontal();

        layout.BeginVertical();
        layout.Add(initialStateControl);
        layout.Add(getInitialStateButton);
        layout.Add(configLayout);
        layout.EndVertical();

        layout.Add(outputsControl);

        layout.EndHorizontal();

        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items = {
                layout,
                runButton

                // new StackLayout {
                //     Spacing = 10,
                //     Orientation = Orientation.Horizontal,
                //     Items = {
                //         runButton,
                //     }
                // }
            }
        };
        Resizable = false;

        Studio.RegisterWindow(this);

        Closed += (_, _) => StoreConfig();
        LoadConfig();

        disableControls = [getInitialStateButton, framesControl, axisControl, positionFilterMinControl, positionFilterMaxControl, goalSpeedControl, disabledKeyControl,
            outputSortingControl, rngThresholdControl, rngThresholdSlowControl, inputGenerationTimeControl, appendKeysControl, hideDuplicatesControl];
    }

    private void StoreConfig() {
        float positionFilterMinValue = (float) positionFilterMinControl.Value;
        float positionFilterMaxValue = (float) positionFilterMaxControl.Value;

        // fix for accidentally backwards order
        if (positionFilterMinValue > positionFilterMaxValue) {
            (positionFilterMinValue, positionFilterMaxValue) = (positionFilterMaxValue, positionFilterMinValue);
        }

        cfg.Frames = (int) framesControl.Value;
        cfg.Axis = Enum.Parse<Axis>(axisControl.SelectedKey);
        cfg.PositionFilter = (positionFilterMinValue, positionFilterMaxValue);
        cfg.GoalSpeed = (float) goalSpeedControl.Value;
        cfg.DisabledKey = Enum.Parse<DisabledKey>(disabledKeyControl.SelectedKey);
        cfg.OutputSortingPriority = Enum.Parse<OutputSortingPriority>(outputSortingControl.SelectedKey);
        cfg.RNGThresholdSlow = (int) rngThresholdSlowControl.Value;
        cfg.RNGThreshold = (int) rngThresholdControl.Value;
        cfg.HideDuplicateInputs = hideDuplicatesControl.Checked!.Value;
        cfg.InputGenerationTime = (int) inputGenerationTimeControl.Value * 1000;
        cfg.AppendKeys = appendKeysControl.Text;
        cfg.InitialState = initialState;
        cfg.InitialStateInfo = initialStateControl.Text;

        initialState.Position = cfg.Axis == Axis.X ? initialState.Positions.X : initialState.Positions.Y;
        initialState.Speed = cfg.Axis == Axis.X ? initialState.Speeds.X : initialState.Speeds.Y;
    }
    private void LoadConfig() {
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
                    //
                    //
                    // (progressLabel = new Label { Text = $"Formatting files {finishedTasks} / {totalTasks}..." }),
                    // (progressBar = new ProgressBar { Width = 300 }),
                    // (doneButton = new Button { Text = "Done", Enabled = false }),
                },
            },

            Resizable = false,
            Closeable = false,
            ShowInTaskbar = true,
        };
        progressPopup.Closed += (_, _) => {
            isRunning = false;
            foreach (var control in disableControls) {
                control.Enabled = true;
            }

            progressPopup = null;
        };

        Studio.RegisterDialog(progressPopup, this);

        isRunning = true;
        foreach (var control in disableControls) {
            control.Enabled = false;
        }
        Task.Run(async () => {
            await RunSim();
            await Application.Instance.InvokeAsync(() => doneCancelButton.Text = "Done");

            Console.WriteLine("DONE");

            // Auto-close after 3s
            await Task.Delay(TimeSpan.FromSeconds(3.0f));
            await Application.Instance.InvokeAsync(() => progressPopup?.Close());
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
        StoreConfig();
        ICollection<List<(int frames, char key)>> inputPermutations;
        await Application.Instance.InvokeAsync(() => individualProgressBar.Value = 0);

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

        int RNGThreshold = (generatorKeys = GeneratorKeys()).Length switch {
            3 => cfg.RNGThresholdSlow,
            4 => cfg.RNGThresholdSlow - 2,
            _ => cfg.RNGThreshold
        };

        if (cfg.Frames < RNGThreshold) {
            await Log("Building permutations using sequential method…");
            inputPermutations = await BuildInputPermutationsSequential();
        } else {
            await Log("Building permutations using RNG method…");
            inputPermutations = await BuildInputPermutationsRng();
        }

        if (!isRunning) return; // canceled

        await Log($"Generated permutations: {inputPermutations.Count}");
        await Application.Instance.InvokeAsync(UpdateLayout);

        // store as position and speed dicts, for performance
        var filteredPermutations = new SortedDictionary<float, Dictionary<float, List<List<(int frames, char key)>>>>();
        var speeds = new HashSet<float>();
        Func<List<(int frames, char key)>, (float position, float speed)> simFunction = cfg.Axis == Axis.X ? SimX : SimY;
        await Application.Instance.InvokeAsync(() => individualProgressBar.Value = inputPermutations.Count);
        int updateInterval = 10000 / cfg.Frames;
        await Log("Simulating inputs…");
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

                if (!isRunning) return;  // canceled
            }
        }

        List<(float position, float speed, List<(int frames, char key)> inputs)> outputPermutations = [];

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
            outputsControl.Items.Clear();
            UpdateLayout();
        });
        i = 0;

        // insert results into output window
        foreach (var inputPermutation in outputPermutations) {
            await Application.Instance.InvokeAsync(() => {
                outputsControl.Items.Add(new ListItem { Text = FormatInputPermutation(inputPermutation), Key = FormatInputPermutationCompact(inputPermutation.inputs) });
            });
            i++;

            if (i == 1000) {
                await Application.Instance.InvokeAsync(UpdateLayout);
                i = 0;
            }
        }

        await Application.Instance.InvokeAsync(() => {
            individualProgressBar.Value = individualProgressBar.MaxValue;

            if (outputsControl.Items.Count == 0) {
                outputsControl.Items.Add(new ListItem { Text = "No solution found", Key = InvalidOutput });
            }

            UpdateLayout();
        });

        await Log("Complete");
    }

    private async Task<ICollection<List<(int frames, char key)>>> BuildInputPermutationsSequential() {
        int expectedPermutations = (int) Math.Pow(generatorKeys.Length, cfg.Frames);
        List<List<(int frames, char key)>> inputPermutations = new(expectedPermutations);
        await Application.Instance.InvokeAsync(() => individualProgressBar.MaxValue = expectedPermutations);
        int lastReportedProgress = 0;
        int i = 0;

        foreach (var permutation in CartesianProduct(generatorKeys, cfg.Frames)) {
            i++;
            List<(int frames, char key)> permutationFormatted = [];
            char? currentInput = null;
            int inputLen = 0;

            // convert messy inputs to the compact format
            foreach (var key in permutation) {
                if (currentInput == null)
                    currentInput = key;

                if (key == currentInput)
                    inputLen++;
                else {
                    permutationFormatted.Add((inputLen, (char) currentInput));
                    currentInput = key;
                    inputLen = 1;
                }
            }

            permutationFormatted.Add((inputLen, (char) currentInput!));
            inputPermutations.Add(permutationFormatted);

            if (i - lastReportedProgress > 10000) {
                lastReportedProgress = i;
                await Application.Instance.InvokeAsync(() => {
                    individualProgressBar.Value = i;
                    UpdateLayout();
                });

                if (!isRunning) return [];  // canceled
            }
        }

        return inputPermutations;
    }

    private async Task<HashSet<List<(int frames, char key)>>> BuildInputPermutationsRng() {
        var inputPermutations = new HashSet<List<(int frames, char key)>>(new ListTupleComparer());
        int keysLen = generatorKeys.Length;
        double maxPermutationsDouble = Math.Pow(generatorKeys.Length, cfg.Frames);
        int maxPermutations = 0;
        bool useMaxPermutations;
        bool brokeFromLoopMax = false;
        int updateInterval = 1000000 / cfg.Frames;
        var random = new FastRandom();
        await Application.Instance.InvokeAsync(() => individualProgressBar.MaxValue = cfg.InputGenerationTime);
        var stopwatch = Stopwatch.StartNew();
        int i = 0;

        if (maxPermutationsDouble <= int.MaxValue) {
            maxPermutations = (int) maxPermutationsDouble;
            useMaxPermutations = true;
        } else {
            useMaxPermutations = false;
        }

        while (true) {
            i++;
            var inputs = new List<(int frames, char key)>();
            int frameCounter = 0;
            char? prevKey = null;

            while (frameCounter < cfg.Frames) {
                int frames = random.Next(1, cfg.Frames - frameCounter + 1);
                frameCounter += frames;
                char selectedKey = generatorKeys[random.Next(keysLen)];

                if (selectedKey == prevKey) {
                    var lastInput = inputs[^1];
                    lastInput.frames += frames;
                    inputs[^1] = lastInput;
                } else {
                    inputs.Add((frames, selectedKey));
                    prevKey = selectedKey;
                }
            }

            inputPermutations.Add(inputs);

            if (i == updateInterval) {
                i = 0;
                int elapsedTime = (int) stopwatch.Elapsed.TotalMilliseconds;
                await Application.Instance.InvokeAsync(() => {
                    individualProgressBar.Value = elapsedTime;
                    UpdateLayout();
                });

                if (!isRunning) return [];  // canceled

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

    private static (float position, float speed) SimX(List<(int frames, char key)> inputs) {
        float x = initialState.Position;
        float speedX = initialState.Speed;
        bool grounded = initialState.OnGround;
        float mult1 = grounded ? 0.0166667f * 1000f : 0.65f * 0.0166667f * 1000f;
        float mult2 = grounded ? 0.0166667f * 400f : 0.65f * 0.0166667f * 400f;
        float max = initialState.Holding ? 70f : 90f;

        foreach (var inputLine in inputs) {
            foreach (char inputKey in Enumerable.Repeat(inputLine.key, inputLine.frames)) {
                // celeste code (from Player.NormalUpdate) somewhat loosely simplified

                if (grounded && inputKey == 'd')
                    speedX = Approach(speedX, 0.0f, 500f * 0.0166667f);
                else {
                    // get input first
                    float moveX = inputKey switch {
                        'l' => -1f,
                        'r' => 1f,
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

    private static (float position, float speed) SimY(List<(int frames, char key)> inputs) {
        float y = initialState.Position;
        float speedY = initialState.Speed;
        float maxFall = initialState.MaxFall;
        int jumpTimer = initialState.JumpTimer;

        foreach (var inputLine in inputs) {
            foreach (char inputKey in Enumerable.Repeat(inputLine.key, inputLine.frames)) {
                // celeste code (from Player.NormalUpdate) somewhat loosely simplified

                // calculate speed
                if (inputKey == 'd' && speedY >= 160f) {
                    maxFall = Approach(maxFall, 240f, 300f * 0.0166667f);
                } else {
                    maxFall = Approach(maxFall, 160f, 300f * 0.0166667f);
                }

                float mult;
                if (Math.Abs(speedY) <= 40f && (inputKey == 'j' || initialState.AutoJump)) {
                    mult = 900f * 0.5f * 0.0166667f;
                } else {
                    mult = 900f * 0.0166667f;
                }

                speedY = Approach(speedY, maxFall, mult);

                if (jumpTimer > 0) {
                    if (inputKey == 'j' || initialState.AutoJump) {
                        speedY = Math.Min(speedY, initialState.Speed);
                    } else {
                        jumpTimer = 0;
                    }
                }

                jumpTimer--;

                // calculate position
                y += speedY * 0.0166667f;
            }
        }

        return ((float) Math.Round(y, 10), (float) Math.Round(speedY, 8));
    }

    private static IEnumerable<IEnumerable<char>> CartesianProduct(char[] source, int repeat) {
        IEnumerable<IEnumerable<char>> seed = [[]];

        for (int i = 0; i < repeat; i++) {
            seed = from seq in seed
                from item in source
                select seq.Concat([item]);
        }

        return seed;
    }

    private class ListTupleComparer : IEqualityComparer<List<(int frames, char key)>> {
        public bool Equals(List<(int frames, char key)>? x, List<(int frames, char key)>? y) {
            if (x!.Count != y!.Count)
                return false;

            for (int i = 0; i < x.Count; i++) {
                if (x[i].frames != y[i].frames || x[i].key != y[i].key)
                    return false;
            }

            return true;
        }

        public int GetHashCode(List<(int frames, char key)> obj) {
            unchecked {
                int hash = 17;

                foreach (var item in obj) {
                    hash = hash * 31 + item.frames;
                    hash = hash * 31 + item.key;
                }

                return hash;
            }
        }
    }

    // directly from Monocle.Calc
    private static float Approach(float val, float target, float maxMove) =>
        val <= target ? Math.Min(val + maxMove, target) : Math.Max(val - maxMove, target);

    private static char[] GeneratorKeys() {
        var keys = new List<char>();

        if (cfg.Axis == Axis.X) {
            keys.AddRange(['\0', 'l', 'r']);

            if (initialState.OnGround)
                keys.Add('d');
        } else
            keys.AddRange(['\0', 'j', 'd']);

        if (cfg.DisabledKey != DisabledKey.None)
            keys.Remove(cfg.DisabledKey.ToString().ToLower().ToCharArray()[0]);

        return keys.ToArray();
    }

    private static string FormatInputPermutation((float position, float speed, List<(int frames, char key)> inputs) inputPermutation) {
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
            string comma = input.key == '\0' ? "" : ",";
            inputsDisplay.Append($"{input.frames}{comma}{char.ToUpper(input.key)} ");
        }

        return inputsDisplay.ToString();
    }

    private static string FormatInputPermutationCompact(List<(int frames, char key)> inputs) {
        var inputsCompact = new StringBuilder();

        foreach (var input in inputs) {
            inputsCompact.Append($"{input.frames},{(input.key == '\0' ? "" : input.key)}\n");
        }

        return inputsCompact.ToString();
    }

    private void OutputsOnSelectedIndexChanged(object? sender, EventArgs e) {
        if (outputsControl.Items.Count == 0) {
            return;
        }

        string selectedItemKey = outputsControl.Items[outputsControl.SelectedIndex].Key!;
        if (selectedItemKey == InvalidOutput) {
            return;
        }

        var appendKeys = appendKeysControl.Text.Where(c => !char.IsWhiteSpace(c));
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

    private void GetInitialState() {
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
            Positions = (gameState.Player.Position.X + gameState.Player.PositionRemainder.X, gameState.Player.Position.Y + gameState.Player.PositionRemainder.Y),
            Speeds = (gameState.Player.Speed.X, gameState.Player.Speed.Y),
            OnGround = gameState.Player is { OnGround: true, Speed.Y: >= 0f },
            Holding = gameState.Player.IsHolding,
            JumpTimer = gameState.Player.JumpTimer,
            AutoJump = gameState.Player.AutoJump,
            MaxFall = gameState.Player.MaxFall,
            ChapterTime = gameState.ChapterTime,
            RoomName = gameState.RoomName,
            PlayerStateName = gameState.PlayerStateName
        };

        initialStateControl.Text = initialState.ToString();
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
                    Max Fall: {MaxFall}
                    State: {PlayerStateName}
                    [{RoomName}] Timer: {ChapterTime}
                    """;
        }
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
