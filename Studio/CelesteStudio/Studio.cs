using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using CelesteStudio.Communication;
using CelesteStudio.Dialog;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.Drawing;
using StudioCommunication;
using FontDialog = CelesteStudio.Dialog.FontDialog;

namespace CelesteStudio;

public sealed class Studio : Form {
    public static Studio Instance = null!;
    public static Version Version { get; private set; } = null!;
    
    public static readonly CommunicationWrapper CommunicationWrapper = new();
    
    // Some platforms report the window larger than it actually is, so there need to be some offsets.
    // The values are chosen by fine-tuning manually.
    public static float BorderBottomOffset {
        get {
            if (Eto.Platform.Instance.IsWpf)
                return 57.0f;
            if (Eto.Platform.Instance.IsGtk)
                return 30.0f;
            if (Eto.Platform.Instance.IsMac)
                return 22.0f;
            return 0.0f;
        }
    }
    public static int WidthRightOffset {
        get {
            if (Eto.Platform.Instance.IsWpf)
                return 16;
            return 0;
        }
    }

    public readonly Editor Editor;
    private readonly Scrollable EditorScrollable;
    private readonly GameInfoPanel GameInfoPanel;

    private string TitleBarText => Editor.Document.FilePath == Document.TemporaryFile 
        ? $"<Unsaved> - Studio v{Version.ToString(3)}" 
        : $"{Editor.Document.FileName}{(Editor.Document.Dirty ? "*" : string.Empty)} - Studio v{Version.ToString(3)}   {Editor.Document.FilePath}";
    
    public Studio() {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version!;
        Icon = Icon.FromResource("Icon.ico"); 
        
        Settings.Load();
        
        if (!Settings.Instance.LastLocation.IsZero) {
            Location = Settings.Instance.LastLocation;
        }
        Size = Settings.Instance.LastSize;
        
        // Setup editor
        {
            EditorScrollable = new Scrollable {
                Width = Size.Width,
                Height = Size.Height,
            };
            Editor = new Editor(Document.Dummy, EditorScrollable);
            EditorScrollable.Content = Editor;

            // On GTK, prevent the scrollable from reacting to Home/End
            if (Eto.Platform.Instance.IsGtk) {
                EditorScrollable.KeyDown += (_, e) => e.Handled = true;
            }
            
            GameInfoPanel = new GameInfoPanel();
            
            Content = new StackLayout {
                Padding = 0,
                Items = {
                    EditorScrollable,
                    GameInfoPanel
                }
            };
            
            SizeChanged += (_, _) => RecalculateLayout();
            
            ApplySettings();
            Settings.Changed += ApplySettings;
            
            // Re-open last file if possible
            if (Settings.Instance.RecentFiles.Count > 0 && !string.IsNullOrWhiteSpace(Settings.Instance.RecentFiles[0]) && File.Exists(Settings.Instance.RecentFiles[0]))
                OpenFile(Settings.Instance.RecentFiles[0]);
            else
                OnNewFile();
        }
    }
    
    public void RecalculateLayout() {
        GameInfoPanel.Width = Width;
        EditorScrollable.Size = new Size(
            Math.Max(0, Width - WidthRightOffset), 
            Math.Max(0, (int)(Height - GameInfoPanel.Height - BorderBottomOffset)));
    }
    
    private void ApplySettings() {
        Topmost = Settings.Instance.AlwaysOnTop;
        Menu = CreateMenu(); // Recreate menu to reflect changes
    }
    
    private MenuBar CreateMenu() {
        const int minDecimals = 2;
        const int maxDecimals = 12;
        const int minFastForwardSpeed = 2;
        const int maxFastForwardSpeed = 30;
        const float minSlowForwardSpeed = 0.1f;
        const float maxSlowForwardSpeed = 0.9f;
        
        // NOTE: Index 0 is the recent files is the current file, so that is skipped
        var openPreviousFile = MenuUtils.CreateAction("Open &Previous File", Keys.Alt | Keys.Left, () => {
            OpenFile(Settings.Instance.RecentFiles[1]);
        });
        openPreviousFile.Enabled = Settings.Instance.RecentFiles.Count > 1;
        
        var recentFilesMenu = new SubMenuItem { Text = "Open &Recent" };
        for (int i = 1; i < Settings.Instance.RecentFiles.Count; i++) {
            string filePath = Settings.Instance.RecentFiles[i];
            recentFilesMenu.Items.Add(MenuUtils.CreateAction(filePath, Keys.None, () => OpenFile(filePath)));
        }
        recentFilesMenu.Items.Add(new SeparatorMenuItem());
        recentFilesMenu.Items.Add(new Command((_, _) => {
            var confirm = MessageBox.Show("Are you sure you want to clear your recent files list?", MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.No);
            if (confirm == DialogResult.Yes) {
                Settings.Instance.ClearRecentFiles();
                Menu = CreateMenu();
            }
        }) { MenuText = "Clear" });
        recentFilesMenu.Enabled = Settings.Instance.RecentFiles.Count > 1;
        
        var backupsMenu = new SubMenuItem { Text = "Open &Backup" };
        var backupDir = Editor.Document.BackupDirectory;
        var backupFiles = Directory.Exists(backupDir) ? Directory.GetFiles(backupDir) : [];
        for (int i = 0; i < backupFiles.Length; i++) {
            if (i >= 20 && backupFiles.Length - i >= 2) { // Only trigger where there are also at least 2 more files left
                backupsMenu.Items.Add(new ButtonMenuItem { Text = $"{backupFiles.Length - i} files remaining...", Enabled = false });
                break;
            }
            
            string filePath = backupFiles[i];
            backupsMenu.Items.Add(MenuUtils.CreateAction(Path.GetFileName(filePath), Keys.None, () => OpenFile(filePath)));
        }
        backupsMenu.Items.Add(new SeparatorMenuItem());
        backupsMenu.Items.Add(new Command((_, _) => ProcessHelper.OpenInDefaultApp(backupDir)) { MenuText = "Show All Files" });
        backupsMenu.Items.Add(new Command((_, _) => {
            var confirm = MessageBox.Show("Are you sure you want to delete all backups for this file?", MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.No);
            if (confirm == DialogResult.Yes) {
                foreach (var file in backupFiles) {
                    File.Delete(file);
                }
                Menu = CreateMenu();
            }
        }) { MenuText = "Delete All Files" });
        backupsMenu.Enabled = backupFiles.Length != 0;
        
        var menu = new MenuBar {
            Items = {
                new SubMenuItem { Text = "&File", Items = {
                    MenuUtils.CreateAction("&New File", Application.Instance.CommonModifier | Keys.N, OnNewFile),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("&Open File...", Application.Instance.CommonModifier | Keys.O, OnOpenFile),
                    openPreviousFile,
                    recentFilesMenu,
                    backupsMenu,
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("Save", Application.Instance.CommonModifier | Keys.S, OnSaveFile),
                    MenuUtils.CreateAction("&Save As...", Application.Instance.CommonModifier | Keys.Shift | Keys.S, OnSaveFileAs),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("&Integrate Read Files"),
                    MenuUtils.CreateAction("&Convert to LibTAS Movie..."),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateAction("&Record TAS...", Keys.None, () => {
                        if (!CommunicationWrapper.Connected) {
                            MessageBox.Show("This feature requires the support of the CelesteTAS mod, please launch the game.", MessageBoxButtons.OK);
                            return;
                        }
                        
                        RecordDialog.Show();
                    }),
                }},
                new SubMenuItem {Text = "&Settings", Items = {
                    MenuUtils.CreateSettingToggle("&Auto Save File", nameof(Settings.AutoSave)),
                    MenuUtils.CreateSettingToggle("&Send Inputs to Celeste", nameof(Settings.SendInputsToCeleste), Application.Instance.CommonModifier | Keys.D),
                    MenuUtils.CreateSettingToggle("Auto Remove Mutually Exclusive Actions", nameof(Settings.AutoRemoveMutuallyExclusiveActions)),
                    MenuUtils.CreateSettingToggle("Show Game Info", nameof(Settings.ShowGameInfo)),
                    MenuUtils.CreateSettingToggle("Always on Top", nameof(Settings.AlwaysOnTop)),
                    MenuUtils.CreateSettingToggle("Word Wrap Comments", nameof(Settings.WordWrapComments)),
                    new SubMenuItem {Text = "Automatic Backups", Items = {
                        MenuUtils.CreateSettingToggle("Enabled", nameof(Settings.AutoBackupEnabled)),
                        MenuUtils.CreateSettingNumberInput("Backup Rate (minutes)", nameof(Settings.AutoBackupRate), 0, int.MaxValue, 1),
                        MenuUtils.CreateSettingNumberInput("Backup File Count", nameof(Settings.AutoBackupCount), 0, int.MaxValue, 1),
                    }},
                    MenuUtils.CreateAction("Snippets...", Keys.None, SnippetDialog.Show),
                    MenuUtils.CreateAction("Font...", Keys.None, FontDialog.Show),
                    MenuUtils.CreateSettingEnum<ThemeType>("Theme", nameof(Settings.ThemeType), ["Light", "Dark"]),
                    MenuUtils.CreateAction("Open Settings File...", Keys.None, () => ProcessHelper.OpenInDefaultApp(Settings.SettingsPath)),
                }},
                new SubMenuItem {Text = "&Toggles", Items = {
                    MenuUtils.CreateToggle("&Hitboxes", CommunicationWrapper.GetHitboxes, CommunicationWrapper.ToggleHitboxes),
                    MenuUtils.CreateToggle("&Trigger Hitboxes", CommunicationWrapper.GetTriggerHitboxes, CommunicationWrapper.ToggleTriggerHitboxes),
                    MenuUtils.CreateToggle("Unloaded Room Hitboxes", CommunicationWrapper.GetUnloadedRoomsHitboxes, CommunicationWrapper.ToggleUnloadedRoomsHitboxes),
                    MenuUtils.CreateToggle("Camera Hitboxes", CommunicationWrapper.GetCameraHitboxes, CommunicationWrapper.ToggleCameraHitboxes),
                    MenuUtils.CreateToggle("&Simplified Hitboxes", CommunicationWrapper.GetSimplifiedHitboxes, CommunicationWrapper.ToggleSimplifiedHitboxes),
                    MenuUtils.CreateToggle("&Actual Collide Hitboxes", CommunicationWrapper.GetActualCollideHitboxes, CommunicationWrapper.ToggleActualCollideHitboxes),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateToggle("&Simplified &Graphics", CommunicationWrapper.GetSimplifiedGraphics, CommunicationWrapper.ToggleSimplifiedGraphics),
                    MenuUtils.CreateToggle("Game&play", CommunicationWrapper.GetGameplay, CommunicationWrapper.ToggleGameplay),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateToggle("&Center Camera", CommunicationWrapper.GetCenterCamera, CommunicationWrapper.ToggleCenterCamera),
                    MenuUtils.CreateToggle("Center Camera Horizontally Only", CommunicationWrapper.GetCenterCameraHorizontallyOnly, CommunicationWrapper.ToggleCenterCameraHorizontallyOnly),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateToggle("&Info HUD", CommunicationWrapper.GetInfoHud, CommunicationWrapper.ToggleInfoHud),
                    MenuUtils.CreateToggle("TAS Input Info", CommunicationWrapper.GetInfoTasInput, CommunicationWrapper.ToggleInfoTasInput),
                    MenuUtils.CreateToggle("Game Info", CommunicationWrapper.GetInfoGame, CommunicationWrapper.ToggleInfoGame),
                    MenuUtils.CreateToggle("Watch Entity Info", CommunicationWrapper.GetInfoWatchEntity, CommunicationWrapper.ToggleInfoWatchEntity),
                    MenuUtils.CreateToggle("Custom Info", CommunicationWrapper.GetInfoCustom, CommunicationWrapper.ToggleInfoCustom),
                    MenuUtils.CreateToggle("Subpixel Indicator", CommunicationWrapper.GetInfoSubpixelIndicator, CommunicationWrapper.ToggleInfoSubpixelIndicator),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateNumberInput("Position Decimals", CommunicationWrapper.GetPositionDecimals, CommunicationWrapper.SetPositionDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Speed Decimals", CommunicationWrapper.GetSpeedDecimals, CommunicationWrapper.SetSpeedDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Velocity Decimals", CommunicationWrapper.GetVelocityDecimals, CommunicationWrapper.SetVelocityDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Angle Decimals", CommunicationWrapper.GetAngleDecimals, CommunicationWrapper.SetAngleDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Custom Info Decimals", CommunicationWrapper.GetCustomInfoDecimals, CommunicationWrapper.SetCustomInfoDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateNumberInput("Subpixel Indicator Decimals", CommunicationWrapper.GetSubpixelIndicatorDecimals, CommunicationWrapper.SetSubpixelIndicatorDecimals, minDecimals, maxDecimals, 1),
                    MenuUtils.CreateToggle("Unit of Speed", CommunicationWrapper.GetSpeedUnit, CommunicationWrapper.ToggleSpeedUnit),
                    new SeparatorMenuItem(),
                    MenuUtils.CreateNumberInput("Fast Forward Speed", CommunicationWrapper.GetFastForwardSpeed, CommunicationWrapper.SetFastForwardSpeed, minFastForwardSpeed, maxFastForwardSpeed, 1),
                    MenuUtils.CreateNumberInput("Slow Forward Speed", CommunicationWrapper.GetSlowForwardSpeed, CommunicationWrapper.SetSlowForwardSpeed, minSlowForwardSpeed, maxSlowForwardSpeed, 0.1f),
                }},
            },
            ApplicationItems = {
                // application (OS X) or file menu (others)
            },
            QuitItem = MenuUtils.CreateAction("Quit", Keys.None, Application.Instance.Quit),
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => new AboutDialog().ShowDialog(this)),
            IncludeSystemItems = MenuBarSystemItems.None,
        };
        
        // The "About" is automatically inserted
        menu.HelpItems.Insert(0, MenuUtils.CreateAction("Home", Keys.None, () => ProcessHelper.OpenInBrowser("https://github.com/EverestAPI/CelesteTAS-EverestInterop")));
        
        return menu;
    }
    
    protected override void OnClosing(CancelEventArgs e) {
        if (!ShouldDiscardChanges()) {
            e.Cancel = true;
            return;
        }
        
        Settings.Instance.LastLocation = Location;
        Settings.Instance.LastSize = Size;
        Settings.Save();
        
        CommunicationWrapper.SendPath(string.Empty);
        
        base.OnClosing(e);
    }
    
    private bool ShouldDiscardChanges() {
        if (Editor.Document.Dirty || Editor.Document.FilePath == Document.TemporaryFile) {
            var confirm = MessageBox.Show($"You have unsaved changes.{Environment.NewLine}Are you sure you want to discard them?", MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.No);
            return confirm == DialogResult.Yes;
        }
        
        return true;
    }
    
    private string GetFilePickerDirectory() {
        var fallbackDir = string.IsNullOrWhiteSpace(Settings.Instance.LastSaveDirectory)
            ? Path.Combine(Directory.GetCurrentDirectory(), "TAS Files")
            : Settings.Instance.LastSaveDirectory;
        
        var dir = Editor.Document.FilePath == Document.TemporaryFile
            ? fallbackDir
            : Path.GetDirectoryName(Editor.Document.FilePath) ?? fallbackDir;
        
        if (!Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }
        
        return dir;
    }
    
    private void OnNewFile() {
        if (!ShouldDiscardChanges())
            return;
        
        string initText = $"RecordCount: 1{Document.NewLine}";
        if (CommunicationWrapper.Connected) {
            if (CommunicationWrapper.Server.GetDataFromGame(GameDataType.ConsoleCommand, true) is { } simpleConsoleCommand) {
                initText += $"{Document.NewLine}{simpleConsoleCommand}{Document.NewLine}   1{Document.NewLine}";
                if (CommunicationWrapper.Server.GetDataFromGame(GameDataType.ModUrl) is { } modUrl) {
                    initText = modUrl + initText;
                }
            }
        }
        initText += $"{Document.NewLine}#Start{Document.NewLine}";
        
        File.WriteAllText(Document.TemporaryFile, initText);
        OpenFile(Document.TemporaryFile);
    }
    
    private void OnOpenFile() {
        if (!ShouldDiscardChanges())
            return;
        
        var dialog = new OpenFileDialog {
            Filters = { new FileFilter("TAS", ".tas") },
            MultiSelect = false,
            Directory = new Uri(GetFilePickerDirectory()),
        };
        
        if (dialog.ShowDialog(this) == DialogResult.Ok) {
            OpenFile(dialog.Filenames.First());
        }
    }
    
    private void OpenFile(string filePath) {
        if (filePath == Editor.Document.FilePath)
            return;
        
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            Settings.Instance.AddRecentFile(filePath);
        
        CommunicationWrapper.WriteWait();
        
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
        Menu = CreateMenu(); // Recreate menu to reflect changed "Recent Files"
        
        CommunicationWrapper.SendPath(Editor.Document.FilePath);
        
        if (filePath != Document.TemporaryFile) {
            Settings.Instance.LastSaveDirectory = Path.GetDirectoryName(filePath)!;
        }
        
        void UpdateTitle(Document _0, CaretPosition _1, CaretPosition _2) {
            Title = TitleBarText;
        }
    }
    
    private void OnSaveFile() {
        if (Editor.Document.FilePath == Document.TemporaryFile) {
            OnSaveFileAs();
            return;
        }
        
        Editor.Document.Save();
        Title = TitleBarText;
    }
    
    private void OnSaveFileAs() {
        var dialog = new SaveFileDialog {
            Filters = { new FileFilter("TAS", ".tas") },
            Directory = new Uri(GetFilePickerDirectory()),
        };
        
        if (dialog.ShowDialog(this) != DialogResult.Ok) 
            return;
        
        var filePath = dialog.FileName;
        if (Path.GetExtension(filePath) != ".tas")
            filePath += ".tas";
        
        CommunicationWrapper.WriteWait();
        
        Editor.Document.FilePath = filePath;
        Editor.Document.Save();
        Title = TitleBarText;
        
        CommunicationWrapper.SendPath(Editor.Document.FilePath);
    }
}