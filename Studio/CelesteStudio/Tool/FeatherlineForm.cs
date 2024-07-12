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

    private readonly TextArea checkpoints;
    private readonly TextArea initialInputs;
    private readonly TextArea customHitboxes;
    private readonly Button run;
    private readonly Button copyOutput;

    public FeatherlineForm() {
        Title = $"Featherline - v{Version}";
        Icon = Studio.Instance.Icon;
        var aboutDialog = new AboutDialog {
            ProgramName = "Jadderline",
            ProgramDescription = "Utility for doing an optimal jelly ladder.",
            Version = Version,

            Developers = ["atpx8", "EllaTAS", "Kataiser", "Mika", "psyGamer", "TheRoboMan", "tntfalle"],
            Logo = Icon,
        };
        Menu = new MenuBar { // TODO: add featherline stuff (mainly settings and help window)
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => aboutDialog.ShowDialog(this)),
        };
        const int rowWidth = 200; // Will probably need to adjust
        checkpoints = new TextArea { Wrap = true, Font = FontManager.EditorFontRegular, Width = rowWidth };
        initialInputs = new TextArea { Wrap = true, Font = FontManager.EditorFontRegular, Width = rowWidth };
        customHitboxes = new TextArea { Wrap = true, Font = FontManager.EditorFontRegular, Width = rowWidth };
        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        layout.BeginHorizontal();
        layout.BeginVertical();
        layout.AddCentered(new Label { Text = "Feather Checkpoints" });
        layout.AddCentered(checkpoints);
        layout.EndBeginVertical();
        layout.AddCentered(new Label { Text = "(Optional) Initial Inputs" });
        layout.AddCentered(initialInputs);
        layout.EndBeginVertical();
        layout.AddCentered(new Label {Text = "Custom Killboxes and Colliders" });
        layout.AddCentered(customHitboxes);
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
