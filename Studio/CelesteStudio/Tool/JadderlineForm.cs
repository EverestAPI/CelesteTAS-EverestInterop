using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Tool;

public sealed class JadderlineForm : Form {
    private const string Version = "1.0.1";
    
    private readonly NumericStepper playerPos;
    private readonly NumericStepper playerSpeed;
    private readonly NumericStepper jelly2Pos;
    private readonly NumericStepper ladders;
    private readonly DropDown direction;
    private readonly CheckBox moveOnly;
    private readonly TextBox additionalInputs;
    
    private readonly TextArea output;
    private readonly Button run;
    private readonly Button copyOutput;
    
    public JadderlineForm() {
        Title = $"Jadderline - v{Version}";
        Icon = Studio.Instance.Icon;
        
        var aboutDialog = new AboutDialog {
            ProgramName = "Jadderline",
            ProgramDescription = "Utility for doing an optimal jelly ladder.",
            Version = Version,
            
            Developers = ["atpx8", "psyGamer"],
            Logo = Icon,
        };
        
        Menu = new MenuBar {
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => aboutDialog.ShowDialog(this)),
        };
        
        const int rowWidth = 200;
        
        playerPos = new NumericStepper { DecimalPlaces = CommunicationWrapper.GetPositionDecimals(), Width = rowWidth };
        playerSpeed = new NumericStepper { DecimalPlaces = CommunicationWrapper.GetSpeedDecimals(), Width = rowWidth };
        jelly2Pos = new NumericStepper { DecimalPlaces = CommunicationWrapper.GetCustomInfoDecimals(), Width = rowWidth };
        ladders = new NumericStepper { MinValue = 2, DecimalPlaces = 0, Width = rowWidth };
        direction = new DropDown { 
            Items = {
                new ListItem { Text = "Left" },
                new ListItem { Text = "Right" },
            },
            SelectedKey = "Right", 
            Width = rowWidth 
        };
        moveOnly = new CheckBox { Width = rowWidth };
        additionalInputs = new TextBox { Width = rowWidth };
        output = new TextArea { ReadOnly = true, Font = FontManager.EditorFontRegular, Width = 250 };
        
        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        layout.BeginHorizontal();
        
        layout.BeginVertical();
        layout.BeginHorizontal();
        layout.AddCentered(new Label { Text = "Player X Position" });
        layout.Add(playerPos);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Player X Speed" });
        layout.Add(playerSpeed);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "2nd Jelly X Position" });
        layout.Add(jelly2Pos);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Re-grab Count" });
        layout.Add(ladders);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Direction" });
        layout.Add(direction);
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Additional Inputs" });
        layout.Add(additionalInputs); // TODO: Convert this into a dropdown with all reasonable values?
        layout.EndBeginHorizontal();
        layout.AddCentered(new Label { Text = "Move-Only Inputs" });
        layout.Add(moveOnly);
        layout.EndHorizontal();
        layout.EndVertical();
        
        layout.Add(output);
        
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
                        (copyOutput = new Button((_, _) => CopyOutput()) { Text = "Copy Output", Width = 150, Enabled = false }),
                    }
                }
            }
        };
        Resizable = false;
    }
    
    private void Run() {
        output.Text = string.Empty;
        run.Enabled = false;
        copyOutput.Enabled = false;
        
        // Get the parameters here and capture them for the task, since it apparently can't access the fields?
        float playerPosValue = (float)playerPos.Value;
        float playerSpeedValue = (float)playerSpeed.Value;
        float jelly2PosValue = (float)jelly2Pos.Value;
        int laddersValue = (int)ladders.Value;
        bool directionValue = direction.SelectedKey == "Right";
        bool moveOnlyValue = moveOnly.Checked!.Value;
        var additionalActions = Actions.None;
        foreach (char c in additionalInputs.Text) {
            additionalActions |= c.ActionForChar();
        }
        
        Task.Run(() => {
            try {
                string result = Run(playerPosValue, playerSpeedValue, jelly2PosValue, laddersValue, directionValue, moveOnlyValue, additionalActions);

                Application.Instance.Invoke(() => {
                    run.Enabled = true;
                    copyOutput.Enabled = true;
                    return output.Text = result;
                });
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                
                Application.Instance.Invoke(() => {
                    run.Enabled = true;
                    copyOutput.Enabled = false;
                    MessageBox.Show($"Failed to calculate optimal jelly ladder:{Environment.NewLine}{ex.Message}", MessageBoxType.Error);
                });
            }
        });
    }
    private void CopyOutput() {
        Clipboard.Instance.Clear();
        Clipboard.Instance.Text = output.Text;
    }
    
    #region Algorithm - by atpx8
    
    const float DeltaTime = 0.0166667f;
    const float FrictionNorm = 650f;
    const float FrictionOverMax = 260f;
    const float FrictionNormHold = FrictionNorm / 2;
    const float FrictionOverMaxHold = FrictionOverMax / 2;

    // Most inputs are self explanatory, though for direction, false is left and true is right
    // Additionally, jelly2 is the one on cooldown (jelly1 is the one about to be grabbed, and as such, doesnt need to be inputted)
    // The additional inputs may or may not need commas, not 100% sure
    // This doesnt go back a frame if an impossible frame is reached, but ive never seen it ever go back in jadderline so its not too much of a priority currently
    public static string Run(float playerPos, float playerSpeed, float jelly2Pos, int ladders, bool direction, bool moveOnly, Actions additionalActions) {
        if (ladders < 2) { // Because we calculate the jelly ladders in 2 regrab windows
            throw new ArgumentException("Must calculate at least 2 ladders");
        }
        float jelly1Pos = playerPos; // Since this is the one we are about to grab
        // Get all 262144 candidates for inputs
        List<(bool[], bool[])> potential = new();
        for (int j = 0; j < 512; j++) {
            for (int k = 0; k < 512; k++) {
                potential.Add((ToBits(j), ToBits(k)));
            }
        }
        List<float> results = new(new float[262144]);
        List<bool[]> inputs = new();
        for (int i = 0; i < ladders; i++) {
            // Remove last entry since we arent at the end yet
            if (inputs.Count != 0) {
                inputs.RemoveAt(inputs.Count - 1);
            }
            Parallel.For(0, 262144, j => {
                results[j] = Eval(potential[j], playerPos, playerSpeed, jelly1Pos, jelly2Pos, direction);
            });
            int max;
            if (direction) {
                max = results.IndexOf(results.Max());
            } else {
                max = results.IndexOf(results.Min());
            }
            if (float.IsNegativeInfinity(results[max]) && float.IsPositiveInfinity(results[max])) {
                throw new ArgumentException("Malformed input or impossible jelly ladder"); // Is this actually the right exception to use? No clue
            }
            inputs.Add(potential[max].Item1);
            inputs.Add(potential[max].Item2);
            (playerPos, playerSpeed, jelly1Pos) = MoveVars(potential[max].Item1, playerPos, playerSpeed, jelly1Pos, direction); // Save the result of the chosen input
            jelly2Pos = float.Round(jelly1Pos); // Make jelly1 the new jelly2
            jelly1Pos = playerPos;
        }
        return Format(inputs, moveOnly, direction, additionalActions);
    }

    // Gets the distance jelly1 has moved while ensuring the player can still grab jelly2
    // Yes this code does kind of suck but it works
    private static float Eval((bool[], bool[]) inputs, float playerPos, float playerSpeed, float jelly1Pos, float jelly2Pos, bool direction) {
        (float playerPosNew, float playerSpeedNew, float jelly1PosNew) = MoveVars(inputs.Item1, playerPos, playerSpeed, jelly1Pos, direction);
        bool wentOver; // Went past jelly
        float jelly2PosNew = float.Round(jelly1PosNew); // New jelly2 position
        jelly1PosNew = playerPosNew;
        if (playerPosNew >= jelly2Pos + 13.5f || playerPosNew < jelly2Pos - 13.5f) {
            if (direction) {
                return float.NegativeInfinity;
            } else {
                return float.PositiveInfinity;
            }
        }
        (playerPosNew, _, _) = MoveVars(inputs.Item2, playerPosNew, playerSpeedNew, jelly1PosNew, direction);  
        if (playerPosNew >= jelly2PosNew + 13.5f || playerPosNew < jelly2PosNew - 13.5f) {
            if (direction) {
                return float.NegativeInfinity;
            } else {
                return float.PositiveInfinity;
            }
        } else {
            return playerPosNew - playerPos;
        }
    }

    // Actually calculates the inputs
    private static (float, float, float) MoveVars(bool[] inputs, float playerPos, float playerSpeed, float jelly1Pos, bool direction) {
        // Frame of movement on last frame of StPickup
        playerPos += playerSpeed * DeltaTime;
        // 8 frames of holding the jelly
        for (int i = 0; i < 8; i++) {
            (playerPos, playerSpeed) = MoveStep(inputs[i], playerPos, playerSpeed, true, direction);
        }
        // Release jelly1, which will be at the player's current positition when dropped
        jelly1Pos = playerPos;
        // Frame of movement when you release the jelly
        (playerPos, playerSpeed) = MoveStep(inputs[8], playerPos, playerSpeed, false, direction);
        return (playerPos, playerSpeed, jelly1Pos);
    }

    // Calculates one frame of movement
    private static (float, float) MoveStep(bool input, float playerPos, float playerSpeed, bool holding, bool direction) {
        float frictionNorm;
        float frictionOverMax;
        float max;
        if (holding) {
            frictionNorm = FrictionNormHold;
            frictionOverMax = FrictionOverMaxHold;
            max = 108f;
        } else {
            frictionNorm = FrictionNorm;
            frictionOverMax = FrictionOverMax;
            max = 90f;
        }
        float mult;
        if (direction) {
            mult = 1f;
        } else {
            mult = -1f;
        }
        if (!input) { // Holding neutral
            playerSpeed -= frictionNorm * DeltaTime * mult;
            if (playerSpeed * mult < 0f) {
                playerSpeed = 0f;
            }
        } else if (playerSpeed * mult <= max) { // Coming up to max speed
            playerSpeed += frictionNorm * DeltaTime * mult;
            if (playerSpeed * mult > max) {
                playerSpeed = max * mult;
            }
        } else { // Over max speed
            playerSpeed -= frictionOverMax * DeltaTime * mult;
            if (playerSpeed * mult < max) {
                playerSpeed = max * mult;
            }
        }
        playerPos += playerSpeed * DeltaTime;
        return (playerPos, playerSpeed);
    }

    // Converts an int into a bool[9]
    private static bool[] ToBits(int num) {
        bool[] bits = new bool[9];
        for (int i = 0; i < 9; i++) {
            bits[i] = (num & 1) != 0;
            num >>= 1;
        }
        return bits;
    }


    // Formats the inputs to be copy and pasted into Studio
    private static string Format(List<bool[]> inputs, bool moveOnly, bool direction, Actions additionalActions) {
        var result = new StringBuilder();

        foreach (var input in inputs) {
            List<(int Frames, bool HoldDir)> formatted = [(13, false)];
            for (int i = 0; i < 8; i++) {
                int last = formatted.Count - 1;
                if (formatted[last].HoldDir == input[i]) {
                    formatted[last] = (formatted[last].Frames + 1, input[i]);
                } else {
                    formatted.Add((1, input[i]));
                }
            }

            foreach (var f in formatted) {
                var actionLine = new ActionLine { Frames = f.Frames, Actions = Actions.Grab | additionalActions };
                if (f.HoldDir) {
                    if (moveOnly) {
                        actionLine.Actions |= Actions.MoveOnly | (direction ? Actions.RightMoveOnly : Actions.LeftMoveOnly);
                    } else {
                        actionLine.Actions |= direction ? Actions.Right : Actions.Left;
                    }
                }
                
                result.AppendLine(actionLine.ToString());
            }
            
            var dropActionLine = new ActionLine { Frames = 1, Actions = additionalActions };
            if (moveOnly) {
                dropActionLine.Actions |= Actions.MoveOnly | Actions.DownMoveOnly;
                if (input[8]) {
                    dropActionLine.Actions |= direction ? Actions.RightMoveOnly : Actions.LeftMoveOnly;
                }
            } else {
                dropActionLine.Actions |= Actions.Down;
                if (input[8]) {
                    dropActionLine.Actions |= direction ? Actions.Right : Actions.Left;
                }
            }
            result.AppendLine(dropActionLine.ToString());
        }

        return result.ToString();
    }
    
    #endregion
}