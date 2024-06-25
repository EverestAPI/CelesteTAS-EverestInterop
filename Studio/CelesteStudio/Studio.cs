using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.Drawing;
using StudioCommunication;

namespace CelesteStudio;

public sealed class Studio : Form {
    public static Studio Instance = null!;
    public static Version Version { get; private set; } = null!;
    
    public static readonly CelesteService CelesteService = new();
    
    // Some platforms report the window larger than it actually is, so there need to be some offsets.
    // The values are chosen by fine-tuning manually.
    public static float BorderRightOffset {
        get {
            if (Eto.Platform.Instance.IsGtk)
                return 40.0f;
            return 0.0f;
        }
    }
    public static float BorderBottomOffset {
        get {
            if (Eto.Platform.Instance.IsGtk)
                return 30.0f;
            return 0.0f;
        }
    }

    public readonly Editor Editor;
    private readonly Scrollable EditorScrollable;
    private readonly GameInfoPanel GameInfoPanel;

    private string TitleBarText => $"{Editor.Document.FileName}{(Editor.Document.Dirty ? "*" : string.Empty)} - Studio v{Version.ToString(3)}   {Editor.Document.FilePath}";
    
    public Studio() {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version!;
        
        Settings.Load();
        
        // Setup editor
        {
            EditorScrollable = new Scrollable {
                Width = 400,
                Height = 800,
            };
            Editor = new Editor(Document.Dummy, EditorScrollable);
            EditorScrollable.Content = Editor;
            
            GameInfoPanel = new GameInfoPanel();
            
            Content = new StackLayout {
                Padding = 0,
                Items = {
                    EditorScrollable,
                    GameInfoPanel
                }
            };
            
            SizeChanged += (_, _) => RecalculateLayout();
            
            NewFile();
        }
        
        Menu = CreateMenu();
    }
    
    public void RecalculateLayout() {
        GameInfoPanel.Width = Width;
        EditorScrollable.Size = new Size(Width, (int)(Height - GameInfoPanel.Height - BorderBottomOffset));
    }
    
    private MenuBar CreateMenu() {
        const int minDecimals = 2;
        const int maxDecimals = 12;
        const int minFastForwardSpeed = 2;
        const int maxFastForwardSpeed = 30;
        const float minSlowForwardSpeed = 0.1f;
        const float maxSlowForwardSpeed = 0.9f;
        
        var quitCommand = new Command {MenuText = "Quit", Shortcut = Application.Instance.CommonModifier | Keys.Q};
        quitCommand.Executed += (_, _) => Application.Instance.Quit();
        
        var aboutCommand = new Command {MenuText = "About..."};
        aboutCommand.Executed += (_, _) => new AboutDialog().ShowDialog(this);
        
        var homeCommand = new Command {MenuText = "Home"};
        homeCommand.Executed += (_, _) => ProcessHelper.OpenInBrowser("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
        
        var menu = new MenuBar {
            Items = {
                new SubMenuItem {Text = "&File", Items = {
                    MenuUtils.CreateAction("&New File", Application.Instance.CommonModifier | Keys.N, NewFile),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("&Open File...", Application.Instance.CommonModifier | Keys.O, () => {
                        var dialog = new OpenFileDialog {
                            Filters = { new FileFilter("TAS", ".tas") },
                            MultiSelect = false,
                            Directory = new Uri(Path.GetDirectoryName(Editor.Document.FilePath)!),
                        };
                        
                        if (dialog.ShowDialog(this) == DialogResult.Ok) {
                            OpenFile(dialog.Filenames.First());
                        }
                    }),
                    MenuUtils.CreateAction("Open &Previous File", Keys.Alt | Keys.O),
                    MenuUtils.CreateAction("Open &Recent"),
                    MenuUtils.CreateAction("Open &Backup"),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("Save", Application.Instance.CommonModifier | Keys.S, SaveFile),
                    MenuUtils.CreateAction("&Save As...", Application.Instance.CommonModifier | Keys.Shift | Keys.S, () => {
                        var dialog = new SaveFileDialog {
                            Filters = { new FileFilter("TAS", ".tas") },
                            Directory = new Uri(Path.GetDirectoryName(Editor.Document.FilePath)!),
                        };
                        
                        if (dialog.ShowDialog(this) == DialogResult.Ok) {
                            var fileName = dialog.FileName;
                            if (Path.GetExtension(fileName) != ".tas")
                                fileName += ".tas";
                            
                            SaveFileAs(fileName);
                        }
                    }),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("&Integrate Read Files"),
                    MenuUtils.CreateAction("&Convert to LibTAS Movie..."),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("&Record TAS..."),
                }},
                new SubMenuItem {Text = "&Settings", Items = {
                    MenuUtils.CreateSettingToggle("&Send Inputs to Celeste", nameof(Settings.SendInputsToCeleste), Application.Instance.CommonModifier | Keys.D),
                    MenuUtils.CreateSettingToggle("Auto Remove Mutually Exclusive Actions", nameof(Settings.AutoRemoveMutuallyExclusiveActions)),
                    MenuUtils.CreateSettingToggle("Show Game Info", nameof(Settings.ShowGameInfo)),
                    MenuUtils.CreateSettingToggle("Always on Top", nameof(Settings.AlwaysOnTop)),
                    new SubMenuItem {Text = "Automatic Backups", Items = {
                        MenuUtils.CreateToggle("Enabled", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                        MenuUtils.CreateNumberInput("Backup Rate (minutes)", CelesteService.GetPositionDecimals, CelesteService.SetPositionDecimals, minDecimals, maxDecimals, 1),
                        MenuUtils.CreateNumberInput("Backup File Count", CelesteService.GetPositionDecimals, CelesteService.SetPositionDecimals, minDecimals, maxDecimals, 1),
                    }},
                    MenuUtils.CreateAction("Font..."),
                    new SubMenuItem {Text = "Theme", Items = {
                        new RadioMenuItem { Text = "Light" },
                        new RadioMenuItem { Text = "Dark" },
                    }},
                    MenuUtils.CreateAction("Open Settings File...", Keys.None, () => ProcessHelper.OpenInEditor(Settings.SavePath)),
                }},
                new SubMenuItem {Text = "&Toggles", Items = {
                    MenuUtils.CreateToggle("&Hitboxes", CelesteService.GetHitboxes, CelesteService.ToggleHitboxes),
                    MenuUtils.CreateToggle("&Trigger Hitboxes", CelesteService.GetTriggerHitboxes, CelesteService.ToggleTriggerHitboxes),
                    MenuUtils.CreateToggle("Unloaded Room Hitboxes", CelesteService.GetUnloadedRoomsHitboxes, CelesteService.ToggleUnloadedRoomsHitboxes),
                    MenuUtils.CreateToggle("Camera Hitboxes", CelesteService.GetCameraHitboxes, CelesteService.ToggleCameraHitboxes),
                    MenuUtils.CreateToggle("&Simplified Hitboxes", CelesteService.GetSimplifiedHitboxes, CelesteService.ToggleSimplifiedHitboxes),
                    MenuUtils.CreateToggle("&Actual Collide Hitboxes", CelesteService.GetActualCollideHitboxes, CelesteService.ToggleActualCollideHitboxes),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateToggle("&Simplified &Graphics", CelesteService.GetSimplifiedGraphics, CelesteService.ToggleSimplifiedGraphics),
                    MenuUtils.CreateToggle("Game&play", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateToggle("&Center Camera", CelesteService.GetCenterCamera, CelesteService.ToggleCenterCamera),
                    MenuUtils.CreateToggle("Center Camera Horizontally Only", CelesteService.GetCenterCameraHorizontallyOnly, CelesteService.ToggleCenterCameraHorizontallyOnly),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateToggle("&Info HUD", CelesteService.GetInfoHud, CelesteService.ToggleInfoHud),
                    MenuUtils.CreateToggle("TAS Input Info", CelesteService.GetInfoTasInput, CelesteService.ToggleInfoTasInput),
                    MenuUtils.CreateToggle("Game Info", CelesteService.GetInfoGame, CelesteService.ToggleInfoGame),
                    MenuUtils.CreateToggle("Watch Entity Info", CelesteService.GetInfoWatchEntity, CelesteService.ToggleInfoWatchEntity),
                    MenuUtils.CreateToggle("Custom Info", CelesteService.GetInfoCustom, CelesteService.ToggleInfoCustom),
                    MenuUtils.CreateToggle("Subpixel Indicator", CelesteService.GetInfoSubpixelIndicator, CelesteService.ToggleInfoSubpixelIndicator),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateNumberInput("Position Decimals", CelesteService.GetPositionDecimals, CelesteService.SetPositionDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Speed Decimals", CelesteService.GetSpeedDecimals, CelesteService.SetSpeedDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Velocity Decimals", CelesteService.GetVelocityDecimals, CelesteService.SetVelocityDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Angle Decimals", CelesteService.GetAngleDecimals, CelesteService.SetAngleDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Custom Info Decimals", CelesteService.GetCustomInfoDecimals, CelesteService.SetCustomInfoDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Subpixel Indicator Decimals", CelesteService.GetSubpixelIndicatorDecimals, CelesteService.SetSubpixelIndicatorDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateToggle("Unit of Speed", CelesteService.GetSpeedUnit, CelesteService.ToggleSpeedUnit),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateNumberInput("Fast Forward Speed", CelesteService.GetFastForwardSpeed, CelesteService.SetFastForwardSpeed, minFastForwardSpeed, maxFastForwardSpeed, 1),
                    MenuUtils.CreateNumberInput("Slow Forward Speed", CelesteService.GetSlowForwardSpeed, CelesteService.SetSlowForwardSpeed, minSlowForwardSpeed, maxSlowForwardSpeed, 0.1f),
                }},
            },
            ApplicationItems = {
                // application (OS X) or file menu (others)
            },
            QuitItem = quitCommand,
            AboutItem = aboutCommand,
            IncludeSystemItems = MenuBarSystemItems.None,
        };
        
        menu.HelpItems.Insert(0, homeCommand); // The "About" is automatically inserted
        
        return menu;
    }
    
    public override void Close() {
        CelesteService.SendPath(string.Empty);
        Settings.Save();
        
        base.Close();
    }
    
    private void NewFile() {
        // TODO: Add "discard changes" prompt
        
        int index = 1;
        string gamePath = Path.Combine(Directory.GetCurrentDirectory(), "TAS Files");
        if (!Directory.Exists(gamePath)) {
            Directory.CreateDirectory(gamePath);
        }
        
        string initText = $"RecordCount: 1{Document.NewLine}";
        if (CelesteService.Connected) {
            if (CelesteService.Server.GetDataFromGame(GameDataType.ConsoleCommand, true) is { } simpleConsoleCommand) {
                initText += $"{Document.NewLine}{simpleConsoleCommand}{Document.NewLine}   1{Document.NewLine}";
                if (CelesteService.Server.GetDataFromGame(GameDataType.ModUrl) is { } modUrl) {
                    initText = modUrl + initText;
                }
            }
        }
        
        initText += $"{Document.NewLine}#Start{Document.NewLine}";
        
        string filePath = Path.Combine(gamePath, $"Untitled-{index}.tas");
        while (File.Exists(filePath) && File.ReadAllText(filePath) != initText) {
            index++;
            filePath = Path.Combine(gamePath, $"Untitled-{index}.tas");
        }
        
        File.WriteAllText(filePath, initText);
        
        OpenFile(filePath);
    }
    
    private void OpenFile(string filePath) {
        if (filePath == Editor.Document.FilePath) {
            return;
        }
        
        CelesteService.WriteWait();
        
        var document = Document.Load(filePath);
        if (document == null) {
            MessageBox.Show($"An unexpected error occured while trying to open the file '{filePath}'", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }
        
        if (Editor.Document is { } doc) {
            doc.Dispose();
            doc.TextChanged -= UpdateTitle;
        }
        
        Editor.Document = document;
        Editor.Document.TextChanged += UpdateTitle;

        Title = TitleBarText;
        
        CelesteService.SendPath(Editor.Document.FilePath);
        
        void UpdateTitle(Document _0, CaretPosition _1, CaretPosition _2) {
            Title = TitleBarText;
        }
    }
    
    private void SaveFile() {
        Editor.Document.Save();
        Title = TitleBarText;
    }
    
    private void SaveFileAs(string filePath) {
        CelesteService.WriteWait();
        
        Editor.Document.FilePath = filePath;
        Editor.Document.Save();
        Title = TitleBarText;
        
        CelesteService.SendPath(Editor.Document.FilePath);
    }
}