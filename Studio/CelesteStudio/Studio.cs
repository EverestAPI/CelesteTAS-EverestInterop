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

    public Editor Editor { get; private set; }
    private string TitleBarText => $"{Editor.Document.FileName}{(Editor.Document.Dirty ? "*" : string.Empty)} - Studio v{Version.ToString(3)}   {Editor.Document.FilePath}";
    
    public Studio() {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version!;
        
        Settings.Load();
        
        // Setup editor
        {
            var scrollable = new Scrollable {
                Width = 400,
                Height = 800,
            };
            Editor = new Editor(Document.Dummy, scrollable);
            scrollable.Content = Editor;
            
            var gameInfoLabel = new Label {
                Text = "Searching...",
                TextColor = Colors.White,
                Font = new(new FontFamily("JetBrains Mono"), 9.0f),
            };
            var gameInfoPanel = new Panel {
                Padding = 5,
                BackgroundColor = Colors.Black,
                Content = gameInfoLabel,
            };
            
            Content = new StackLayout {
                Padding = 0,
                Items = {
                    scrollable,
                    gameInfoPanel
                }
            };
            
            SizeChanged += (_, _) => {
                gameInfoPanel.Width = Width;
                scrollable.Size = new Size(Width, (int)(Height - gameInfoPanel.Height - BorderBottomOffset));
            };
            CelesteService.Server.StateUpdated += _ => Application.Instance.InvokeAsync(UpdateGameInfo);
            CelesteService.Server.Reset += () => Application.Instance.InvokeAsync(UpdateGameInfo);
            
            NewFile();
            
            void UpdateGameInfo() {
                gameInfoLabel.Text = CelesteService.Connected ? CelesteService.State.GameInfo.Trim() : "Searching...";
                
                int extraHeight = gameInfoPanel.Height - gameInfoLabel.Height;
                var newHeight = gameInfoLabel.Font.MeasureString(gameInfoLabel.Text).Height + extraHeight;
                scrollable.Height = (int)(Height - newHeight - BorderBottomOffset);
            }
        }
        
        Menu = CreateMenu();
    }
    
    private MenuBar CreateMenu() {
        static MenuItem CreateToggle(string text, Func<bool> getFn, Action toggleFn) {
            var cmd = new CheckCommand { MenuText = text };
            cmd.Executed += (_, _) => toggleFn();
            
            // TODO: Convert to CheckMenuItem
            return new ButtonMenuItem(cmd);
        }
        
        static MenuItem CreateSettingToggle(string text, string settingName) {
            var property = typeof(Settings).GetField(settingName)!;
            
            var cmd = new CheckCommand { MenuText = text };
            cmd.Checked = (bool)property.GetValue(Settings.Instance)!;
            cmd.Executed += (_, _) => {
                bool value = (bool)property.GetValue(Settings.Instance)!;
                property.SetValue(Settings.Instance, !value);

                Settings.Save();
            };
            
            return new CheckMenuItem(cmd);
        }
        
        static MenuItem CreateNumberInput<T>(string text, Func<T> getFn, Action<T> setFn, T minValue, T maxValue, T step) where T : INumber<T> {
            var cmd = new Command { MenuText = text };
            cmd.Executed += (_, _) => setFn(DialogUtil.ShowNumberInputDialog(text, getFn(), minValue, maxValue, step));
            
            return new ButtonMenuItem(cmd);
        }
        
        static MenuItem CreateAction(string text, Keys shortcut = Keys.None, Action? action = null) {
            var cmd = new Command { MenuText = text, Shortcut = shortcut, Enabled = action != null };
            cmd.Executed += (_, _) => action?.Invoke();
            
            return cmd;
        }
        
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
        homeCommand.Executed += (_, _) => URIHelper.OpenInBrowser("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
        
        var menu = new MenuBar {
            Items = {
                new SubMenuItem {Text = "&File", Items = {
                    CreateAction("&New File", Application.Instance.CommonModifier | Keys.N, NewFile),
                    new SeparatorMenuItem(),
                    CreateAction("&Open File...", Application.Instance.CommonModifier | Keys.O, () => {
                        var dialog = new OpenFileDialog {
                            Filters = { new FileFilter("TAS", ".tas") },
                            MultiSelect = false,
                            Directory = new Uri(Path.GetDirectoryName(Editor.Document.FilePath)!),
                        };
                        
                        if (dialog.ShowDialog(this) == DialogResult.Ok) {
                            OpenFile(dialog.Filenames.First());
                        }
                    }),
                    CreateAction("Open &Previous File", Keys.Alt | Keys.O),
                    CreateAction("Open &Recent"),
                    CreateAction("Open &Backup"),
                    new SeparatorMenuItem(),
                    CreateAction("Save", Application.Instance.CommonModifier | Keys.S, SaveFile),
                    CreateAction("&Save As...", Application.Instance.CommonModifier | Keys.Shift | Keys.S, () => {
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
                    CreateAction("&Integrate Read Files"),
                    CreateAction("&Convert to LibTAS Movie..."),
                    new SeparatorMenuItem(),
                    CreateAction("&Record TAS..."),
                }},
                new SubMenuItem {Text = "&Settings", Items = {
                    CreateSettingToggle("&Send Inputs to Celeste", nameof(Settings.SendInputsToCeleste)),
                    CreateToggle("Auto Remove Mutually Exclusive Actions", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                    CreateToggle("Show Game Info", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                    CreateToggle("Always on Top", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                    new SubMenuItem {Text = "Automatic Backups", Items = {
                        CreateToggle("Enabled", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                        CreateNumberInput("Backup Rate (minutes)", CelesteService.GetPositionDecimals, CelesteService.SetPositionDecimals, minDecimals, maxDecimals, 1),
                        CreateNumberInput("Backup File Count", CelesteService.GetPositionDecimals, CelesteService.SetPositionDecimals, minDecimals, maxDecimals, 1),
                    }},
                    CreateAction("Font..."),
                    new SubMenuItem {Text = "Theme", Items = {
                        new RadioMenuItem { Text = "Light" },
                        new RadioMenuItem { Text = "Dark" },
                    }},
                    CreateAction("Open Settings File..."),
                }},
                new SubMenuItem {Text = "&Toggles", Items = {
                    CreateToggle("&Hitboxes", CelesteService.GetHitboxes, CelesteService.ToggleHitboxes),
                    CreateToggle("&Trigger Hitboxes", CelesteService.GetTriggerHitboxes, CelesteService.ToggleTriggerHitboxes),
                    CreateToggle("Unloaded Room Hitboxes", CelesteService.GetUnloadedRoomsHitboxes, CelesteService.ToggleUnloadedRoomsHitboxes),
                    CreateToggle("Camera Hitboxes", CelesteService.GetCameraHitboxes, CelesteService.ToggleCameraHitboxes),
                    CreateToggle("&Simplified Hitboxes", CelesteService.GetSimplifiedHitboxes, CelesteService.ToggleSimplifiedHitboxes),
                    CreateToggle("&Actual Collide Hitboxes", CelesteService.GetActualCollideHitboxes, CelesteService.ToggleActualCollideHitboxes),
                    new SeparatorMenuItem(),
                    CreateToggle("&Simplified &Graphics", CelesteService.GetSimplifiedGraphics, CelesteService.ToggleSimplifiedGraphics),
                    CreateToggle("Game&play", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                    new SeparatorMenuItem(),
                    CreateToggle("&Center Camera", CelesteService.GetCenterCamera, CelesteService.ToggleCenterCamera),
                    CreateToggle("Center Camera Horizontally Only", CelesteService.GetCenterCameraHorizontallyOnly, CelesteService.ToggleCenterCameraHorizontallyOnly),
                    new SeparatorMenuItem(),
                    CreateToggle("&Info HUD", CelesteService.GetInfoHud, CelesteService.ToggleInfoHud),
                    CreateToggle("TAS Input Info", CelesteService.GetInfoTasInput, CelesteService.ToggleInfoTasInput),
                    CreateToggle("Game Info", CelesteService.GetInfoGame, CelesteService.ToggleInfoGame),
                    CreateToggle("Watch Entity Info", CelesteService.GetInfoWatchEntity, CelesteService.ToggleInfoWatchEntity),
                    CreateToggle("Custom Info", CelesteService.GetInfoCustom, CelesteService.ToggleInfoCustom),
                    CreateToggle("Subpixel Indicator", CelesteService.GetInfoSubpixelIndicator, CelesteService.ToggleInfoSubpixelIndicator),
                    new SeparatorMenuItem(),
                    CreateNumberInput("Position Decimals", CelesteService.GetPositionDecimals, CelesteService.SetPositionDecimals, minDecimals, maxDecimals, 1),
                    CreateNumberInput("Speed Decimals", CelesteService.GetSpeedDecimals, CelesteService.SetSpeedDecimals, minDecimals, maxDecimals, 1),
                    CreateNumberInput("Velocity Decimals", CelesteService.GetVelocityDecimals, CelesteService.SetVelocityDecimals, minDecimals, maxDecimals, 1),
                    CreateNumberInput("Angle Decimals", CelesteService.GetAngleDecimals, CelesteService.SetAngleDecimals, minDecimals, maxDecimals, 1),
                    CreateNumberInput("Custom Info Decimals", CelesteService.GetCustomInfoDecimals, CelesteService.SetCustomInfoDecimals, minDecimals, maxDecimals, 1),
                    CreateNumberInput("Subpixel Indicator Decimals", CelesteService.GetSubpixelIndicatorDecimals, CelesteService.SetSubpixelIndicatorDecimals, minDecimals, maxDecimals, 1),
                    CreateToggle("Unit of Speed", CelesteService.GetSpeedUnit, CelesteService.ToggleSpeedUnit),
                    new SeparatorMenuItem(),
                    CreateNumberInput("Fast Forward Speed", CelesteService.GetFastForwardSpeed, CelesteService.SetFastForwardSpeed, minFastForwardSpeed, maxFastForwardSpeed, 1),
                    CreateNumberInput("Slow Forward Speed", CelesteService.GetSlowForwardSpeed, CelesteService.SetSlowForwardSpeed, minSlowForwardSpeed, maxSlowForwardSpeed, 0.1f),
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