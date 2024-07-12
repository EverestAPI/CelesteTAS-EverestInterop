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

public sealed class FeatherlineForm : Form {
    private const string Version = "0.3.3.1";

    private readonly NumericStepper generations;
    private readonly NumericStepper maxFrames;
    private readonly NumericStepper gensPerTiming;
    private readonly NumericStepper timingShuffles;
    private readonly CheckBox testOnInitial;
    private readonly TextArea checkpoints;
    private readonly TextArea initialInputs;
    private readonly TextArea customHitboxes;
    private readonly TextArea output;
    private readonly Button run;
    private readonly Button copyOutput;

    public FeatherlineForm() {
        Title = $"Featherline - v{Version}";
        Icon = Studio.Instance.Icon;
        Menu = new MenuBar { // TODO: add featherline stuff (mainly settings and help window)
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
                Studio.ShowAboutDialog(new AboutDialog {
                    ProgramName = "Jadderline",
                    ProgramDescription = "Utility for doing an optimal jelly ladder.",
                    Version = Version,

                    Developers = ["atpx8", "EllaTAS", "Kataiser", "Mika", "psyGamer", "TheRoboMan", "tntfalle"],
                    Logo = Icon,
                }, this);
            }),
        };
        const int stepperWidth = 100;
        const int textWidth = 200;
        generations = new NumericStepper { MinValue = 0, MaxValue = 999999, Value = 2000, DecimalPlaces = 0, Width = stepperWidth };
        maxFrames = new NumericStepper { MinValue = 1, MaxValue = 9999, Value = 120, DecimalPlaces = 0, Width = stepperWidth };
        gensPerTiming = new NumericStepper { MinValue = 1, MaxValue = 999999, Value = 150, DecimalPlaces = 0, Width = stepperWidth };
        timingShuffles = new NumericStepper { MinValue = 0, MaxValue = 100, Value = 6, DecimalPlaces = 0, Width = stepperWidth };
        testOnInitial = new CheckBox { Text = "Test Timing On\nInitial Inputs Directly", Checked = false };
        checkpoints = new TextArea { Wrap = true, Font = FontManager.EditorFontRegular, Width = textWidth };
        initialInputs = new TextArea { Wrap = true, Font = FontManager.EditorFontRegular, Width = textWidth };
        customHitboxes = new TextArea { Wrap = true, Font = FontManager.EditorFontRegular, Width = textWidth };
        output = new TextArea { ReadOnly = true, Font = FontManager.EditorFontRegular, Width = textWidth };
        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        layout.BeginHorizontal();
        layout.BeginVertical();
        layout.Add(new Label { Text = "Generations" });
        layout.Add(generations);
        layout.AddSpace();
        layout.Add(new Label { Text = "Max Framecount" });
        layout.Add(maxFrames);
        layout.AddSpace();
        layout.Add(new Label { Text = "Gens Per Tested Timing" });
        layout.Add(gensPerTiming);
        layout.AddSpace();
        layout.Add(new Label { Text = "Timing Shuffle Count" });
        layout.Add(timingShuffles);
        layout.AddSpace();
        layout.Add(testOnInitial);
        layout.EndBeginVertical();
        layout.AddCentered(new Label { Text = "Feather Checkpoints" });
        layout.Add(checkpoints);
        layout.EndBeginVertical();
        layout.AddCentered(new Label { Text = "(Optional) Initial Inputs" });
        layout.Add(initialInputs);
        layout.EndBeginVertical();
        layout.AddCentered(new Label {Text = "Custom Killboxes and Colliders" });
        layout.Add(customHitboxes);
        layout.EndBeginVertical();
        layout.AddCentered(new Label { Text = "Output" });
        layout.Add  (output);
        layout.EndVertical();
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
                    Items = { // TODO: make these lambdas actually do something
                        (run = new Button((_, _) => {}) { Text = "Run", Width = 150 }),
                        (copyOutput = new Button((_, _) => {}) { Text = "Copy Output", Width = 150, Enabled = false }),
                    }
                }
            }
        };
        Resizable = false;  
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }
}
