using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using Featherline;
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
    private readonly Button getInfo;
    private readonly Button run;
    private readonly Button copyOutput;
    private FeatherlineHelpForm? helpForm;

    private bool running = false;

    public FeatherlineForm() {
        Title = $"Featherline - v{Version}";
        Icon = Studio.Instance.Icon;
        CreateMenu();
        FeatherlineSettings.Changed += CreateMenu;
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
                    Items = {
                        (getInfo = new Button((_, _) => GetInfo()) { Text = "Get Game Info", Width = 150}),
                        (run = new Button((_, _) => Toggle()) { Text = "Run", Width = 150, Enabled = false }),
                        (copyOutput = new Button((_, _) => CopyOutput()) { Text = "Copy Output", Width = 150, Enabled = false }),
                    }
                }
            }
        };
        Resizable = false;  
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
    }

    private void CreateMenu() {
        Menu = new MenuBar { // TODO: add help window
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
                Studio.ShowAboutDialog(new AboutDialog {
                    ProgramName = "Jadderline",
                    ProgramDescription = "Utility for doing an optimal jelly ladder.",
                    Version = Version,

                    Developers = ["atpx8", "EllaTAS", "Kataiser", "Mika", "psyGamer", "TheRoboMan", "tntfalle"],
                    Logo = Icon,
                }, this);
            }),
            Items = { // TODO: these need to set featherline settings
                new SubMenuItem { Text = "Settings", Items = {
                    new SubMenuItem { Text = "Genetic Algorithm", Items = {
                        MenuUtils.CreateFeatherlineSettingNumberInput("Population", "Population", 2, 999999, 1),
                        MenuUtils.CreateFeatherlineSettingNumberInput("Generation Survivors", "GenerationSurvivors", 1, 999998, 1),
                        MenuUtils.CreateFeatherlineSettingNumberInput("Mutation Magnitude", "MutationMagnitude", 0f, 180f, 0.1f),
                        MenuUtils.CreateFeatherlineSettingNumberInput("Max Mutation Count", "MaxMutations", 1, 999999, 1),
                    }},
                    new SubMenuItem { Text = "Algorithm Mode", Items = {
                        MenuUtils.CreateFeatherlineSettingToggle("Frame Genes Only", "FrameOnly"),
                        MenuUtils.CreateFeatherlineSettingToggle("Disallow Wall Collision", "DisallowWall"),
                    }},
                    MenuUtils.CreateFeatherlineSettingNumberInput("Simulation Thread Count", "SimulationThreads", -1, 100, 1),
                }},
            },
        };
        Menu.HelpItems.Insert(0, MenuUtils.CreateAction("How To Use", Keys.None, () => {
            helpForm ??= new();
            helpForm.Show();
            helpForm.Closed += (_, _) => helpForm = null;
        }));
    }

    private void GetInfo() {
        run.Enabled = false;
        Featherline.Settings.Info = new List<object>() {
            CommunicationWrapper.GetRawData<(float, float)>("Player.Speed"), // Speed
            CommunicationWrapper.GetRawData<(float, float)>("Player.Position"), // Pos
            CommunicationWrapper.GetRawData<(float, float)>("Player.PositionRemainder"), // PosRemainder
            CommunicationWrapper.GetRawData<float>("Player.starFlySpeedLerp"), // Lerp
            CommunicationWrapper.GetRawData<List<(float, float)>>("CrystalStaticSpinner.Position", true).Concat( // Spinners
            CommunicationWrapper.GetRawData<List<(float, float)>>("DustStaticSpinner.Position", true)).Concat(
            CommunicationWrapper.GetRawData<List<(float, float)>>("FrostHelper.CustomSpinner@FrostTempleHelper.Position", true)).Concat(
            CommunicationWrapper.GetRawData<List<(float, float)>>("VivHelper.Entities.CustomSpinner@VivHelper.Position", true)).Concat(
            CommunicationWrapper.GetRawData<List<(float, float)>>("Celeste.Mod.XaphanHelper.Entities.CustomSpinner@XaphanHelper.Position", true)).ToList(),
            CommunicationWrapper.GetRawData<List<(float, float)>>("Lightning.TopLeft", true), // LightningUL
            CommunicationWrapper.GetRawData<List<(float, float)>>("Lightning.BottomRight", true), // LightningDR
            CommunicationWrapper.GetRawData<List<(float, float)>>("Spikes.TopLeft", true), // SpikeUL
            CommunicationWrapper.GetRawData<List<(float, float)>>("Spikes.BottomRight", true), // SpikeDR
            CommunicationWrapper.GetRawData<List<int>>("Spikes.Direction", true), // SpikeDir
            CommunicationWrapper.GetRawData<(float, float)>("Level.Wind"), // Wind
            CommunicationWrapper.GetRawData<List<(float, float)>>("WindTrigger.Position", true), // WTPos
            CommunicationWrapper.GetRawData<List<int>>("WindTrigger.Pattern", true), // WTPattern
            CommunicationWrapper.GetRawData<List<float>>("WindTrigger.Width", true), // WTWidth
            CommunicationWrapper.GetRawData<List<float>>("WindTrigger.Height", true), // WTHeight
            CommunicationWrapper.GetRawData<List<(float, float)>>("StarJumpBlock.TopLeft", true), // StarJumpUL
            CommunicationWrapper.GetRawData<List<(float, float)>>("StarJumpBlock.BottomRight", true), // StarJumpDR
            CommunicationWrapper.GetRawData<List<bool>>("StarJumpBlock.sinks", true), // StarJumpSinks
            CommunicationWrapper.GetRawData<List<(float, float)>>("JumpthruPlatform.TopLeft", true), // JThruUL
            CommunicationWrapper.GetRawData<List<(float, float)>>("JumpthruPlatform.BottomRight", true), // JThruDR
            CommunicationWrapper.GetRawData<List<(float, float)>>("SidewaysJumpThru.TopLeft", true), // SideJTUL
            CommunicationWrapper.GetRawData<List<(float, float)>>("SidewaysJumpThru.BottomRight", true), // SideJTDR
            CommunicationWrapper.GetRawData<List<bool>>("SidewaysJumpThru.AllowLeftToRight", true), // SideJTIsRight
            CommunicationWrapper.GetRawData<List<bool>>("SidewaysJumpThru.pushPlayer", true), // SideJTPushes
            CommunicationWrapper.GetRawData<List<(float, float)>>("UpsideDownJumpThru.TopLeft", true), // UpsDJTUL
            CommunicationWrapper.GetRawData<List<(float, float)>>("UpsideDownJumpThru.BottomRight", true), // UpsDJTDR
            CommunicationWrapper.GetRawData<List<bool>>("UpsideDownJumpThru.pushPlayer", true), // UpsDJTPushes
            CommunicationWrapper.GetRawData<int>("Level.Bounds.X"), // Bounds
            CommunicationWrapper.GetRawData<int>("Level.Bounds.Y"),
            CommunicationWrapper.GetRawData<int>("Level.Bounds.Width"),
            CommunicationWrapper.GetRawData<int>("Level.Bounds.Height"),
            CommunicationWrapper.GetRawData<string>("Level.Session.LevelData.Solids"), // Solids
        };
        run.Enabled = true;
    }

    private void CopyOutput() {
        Clipboard.Instance.Clear();
        Clipboard.Instance.Text = output.Text;
    }

    private void Toggle() {
        if (!running) {
            running = true;
            run.Text = "Abort";

            Featherline.Settings.Generations = (int) generations.Value;
            Featherline.Settings.Framecount = (int) maxFrames.Value;
            Featherline.Settings.GensPerTiming = (int) gensPerTiming.Value;
            Featherline.Settings.ShuffleCount = (int) timingShuffles.Value;
            Featherline.Settings.TimingTestFavDirectly = (bool) testOnInitial.Checked;
            Featherline.Settings.Checkpoints = checkpoints.Text.Split("\n");
            Featherline.Settings.Favorite = initialInputs.Text;
            Featherline.Settings.ManualHitboxes = customHitboxes.Text.Split("\n");

            Featherline.Settings.Population = FeatherlineSettings.Instance.Population;
            Featherline.Settings.SurvivorCount = FeatherlineSettings.Instance.GenerationSurvivors;
            Featherline.Settings.MutationMagnitude = FeatherlineSettings.Instance.MutationMagnitude;
            Featherline.Settings.MaxMutChangeCount = FeatherlineSettings.Instance.MaxMutations;
            Featherline.Settings.FrameBasedOnly = FeatherlineSettings.Instance.FrameOnly;
            Featherline.Settings.AvoidWalls = FeatherlineSettings.Instance.DisallowWall;

            Task.Run(() => {
                bool runEnd = GAManager.RunAlgorithm(false);
                if (runEnd) {
                    GAManager.EndAlgorithm();
                }
                GAManager.ClearAlgorithmData();
                running = false;
                Application.Instance.Invoke(() => {
                    output.Text = Featherline.Settings.Output;
                    if (Featherline.Settings.Output != "") {
                        copyOutput.Enabled = true;
                    } else {
                        copyOutput.Enabled = false;
                    }
                    run.Text = "Run";
                });
            });
        } else {
            running = false;
            output.Text = "";
            copyOutput.Enabled = false;
            run.Text = "Run";
        }
    }
}


public sealed class FeatherlineHelpForm : Form {
    public FeatherlineHelpForm() {
        Title = "Featherline - Help";
        Icon = Studio.Instance.Icon;
        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        var h1 = new Font(SystemFont.Bold, 20f);
        layout.BeginVertical();
        layout.AddCentered(new Label { Wrap = WrapMode.Word, Text = "Getting started with Featherline", Font = h1 });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "1. Run your TAS up until the frame you featherboost, such as" });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "  26\r\n > 1,R,U", Font = FontManager.EditorFontRegular });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "The TAS should be paused here." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "2. Click the Get Game Info button." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "3. Define a checkpoint at every turn or branching point of the path you want to TAS. Checkpoints are further explained later." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "4. Click the Run button." });
        layout.AddCentered(new Label { Wrap = WrapMode.Word, Text = "Checkpoints", Font = h1 });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- To define a checkpoint, hold your info HUD hotkey and drag while holding right click to draw a rectangle hitbox. Doing that will copy the selected area to your clipboard, where you can paste it to a line on the checkpoints text box." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Checkpoint collision is based on your hurtbox. To define a touch switch or feather as a checkpoint, select an area that perfectly overlaps with its hitbox. Remember to use the pink, bigger hitbox for touch switches." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- The genetic algorithm primarily flies directly towards the next checkpoint. If the next checkpoint is behind a wall of spinners, it will simply fly towards that wall of spinners and try to get as close to the checkpoint as it can that way. That means you should define a checkpoint at every major turn so it knows where to go. If the algorithm messes up at any of the points where progress is reversed, it has to be able to fix itself by simply attempting to fly toward the next checkpoint the entire time." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Making checkpoints really small is not recommended. Making them big does not make the result worse and it only cares about whether you touched the checkpoint or not. When the algorithm at some moment has not reached a checkpoint, it tries to get to it by aiming for its center (the final checkpoint is an exception to this). You can use this to guide the algorithm better by making the checkpoints bigger." });
        layout.AddCentered(new Label { Wrap = WrapMode.Word, Text = "Custom Hitboxes", Font = h1 });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Defined the same way as checkpoints but in the text box on the right." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- A defined hitbox is a killbox by default, based on the green hurtbox." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- To define a collider (based on the red collision box) instead of a killbox, place 'c' after the definition.\r\n   Example: '218, -104, 234, -72 c'" });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Fully static tile entities will automatically be added as colliders behind the scenes, but kevins, falling blocks and others that have the potential to move in some way, you will have to define manually." });
        layout.AddCentered(new Label { Wrap = WrapMode.Word, Text = "Algorithm Facts", Font = h1 });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Sometimes the final results of the algorithm will die by an extremely small amount, like 0.0002 pixels. When this happens, the solution is to change one of the angles before that point by 0.001 manually or by a little bit more if needed." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Each checkpoint collected adds 10000 to fitness. You can use that knowledge to track how the algorithm is doing." });
        layout.AddCentered(new Label { Wrap = WrapMode.Word, Text = "Supported Gameplay Mechanics", Font = h1 });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Anything with a static spinner hitbox, spikes and lightning" });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Wind and wind triggers" });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Dodging or bouncing off walls. Tile entities explained in Custom Hitboxes section." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Jumpthroughs" });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "- Correct physics with room bounds" });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "If you have questions that aren't explained anywhere in this guide, feel free to ping TheRoboMan on the Celeste Discord." });
        layout.Add(new Label { Wrap = WrapMode.Word, Text = "If you experience any issues with the user-interface or other things about this version specifically, ping atpx8 or psyGamer on the Celeste Discord." });
        layout.EndVertical();
        var scrollable = new Scrollable { Content = new Panel { Width = 500, Content = layout }, Width = 520, Height = 400 };
        Content = scrollable;
        Resizable = false;
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
    }
}