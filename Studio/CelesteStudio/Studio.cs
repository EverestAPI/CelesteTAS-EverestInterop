using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using CelesteStudio.Communication;
using CelesteStudio.Dialog;
using CelesteStudio.Editing;
using CelesteStudio.Tool;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.Drawing;
using StudioCommunication;
using FontDialog = CelesteStudio.Dialog.FontDialog;

namespace CelesteStudio;

public sealed class Studio : Form {
    public static Studio Instance = null!;
    public static Version Version { get; private set; } = null!;
    
    // Platform-specific callback to handle new windows
    public readonly Action<Window> WindowCreationCallback;

    public readonly Editor Editor;
    public readonly GameInfoPanel GameInfoPanel;
    private readonly Scrollable EditorScrollable;
    
    private JadderlineForm? jadderlineForm;
    private ThemeEditor? themeEditorForm;

    private string TitleBarText => Editor.Document.FilePath == Document.ScratchFile 
        ? $"<Scratch> - Studio v{Version.ToString(3)}" 
        : $"{Editor.Document.FileName}{(Editor.Document.Dirty ? "*" : string.Empty)} - Studio v{Version.ToString(3)}   {Editor.Document.FilePath}";
    
    public Studio(Action<Window> windowCreationCallback) {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version!;
        Icon = Icon.FromResource("Icon.ico");
        
        WindowCreationCallback = windowCreationCallback;
        
        Settings.Load();
        
        if (!Settings.Instance.LastLocation.IsZero) {
            Location = Settings.Instance.LastLocation;
        }
        Size = Settings.Instance.LastSize;
        
        // Needs to be registered before the editor is created 
        Settings.Changed += ApplySettings;
        
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
            Shown += (_, _) => {
                GameInfoPanel.UpdateLayout();
                RecalculateLayout();
            };
            

            ApplySettings();
            
            // Only enable some settings while connected
            CommunicationWrapper.ConnectionChanged += () => Application.Instance.Invoke(() => {
                CommandInfo.ResetCache();
                Menu = CreateMenu();
            });
            
            // Re-open last file if possible
            if (Settings.Instance.RecentFiles.Count > 0 && !string.IsNullOrWhiteSpace(Settings.Instance.RecentFiles[0]) && File.Exists(Settings.Instance.RecentFiles[0])) {
                OpenFile(Settings.Instance.RecentFiles[0]);
            } else {
                OnNewFile();
            }
        }
        
        CommunicationWrapper.Start();
    }
    
    public void RecalculateLayout() {
        GameInfoPanel.Width = Width;
        EditorScrollable.Size = new Size(
            Math.Max(0, ClientSize.Width), 
            Math.Max(0, (int)(ClientSize.Height - GameInfoPanel.Height)));
    }
    
    private void ApplySettings() {
        Topmost = Settings.Instance.AlwaysOnTop;
        Menu = CreateMenu(); // Recreate menu to reflect changes
        
        CommandInfo.GenerateCommandInfos(Settings.Instance.CommandSeparator switch {
            CommandSeparator.Space => " ",
            CommandSeparator.Comma => ",",
            CommandSeparator.CommaSpace => ", ",
            _ => throw new UnreachableException()
        });
    }
    
    protected override void OnClosing(CancelEventArgs e) {
        if (!ShouldDiscardChanges(checkTempFile: false)) {
            e.Cancel = true;
            return;
        }
        
        Settings.Instance.LastLocation = Location;
        Settings.Instance.LastSize = Size;
        Settings.Save();
        
        CommunicationWrapper.SendPath(string.Empty);
        CommunicationWrapper.Stop();
        
        base.OnClosing(e);
    }
    
    private bool ShouldDiscardChanges(bool checkTempFile = true) {
        if (Editor.Document.Dirty || checkTempFile && Editor.Document.FilePath == Document.ScratchFile) {
            var confirm = MessageBox.Show($"You have unsaved changes.{Environment.NewLine}Are you sure you want to discard them?", MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.No);
            return confirm == DialogResult.Yes;
        }
        
        return true;
    }
    
    private string GetFilePickerDirectory() {
        var fallbackDir = string.IsNullOrWhiteSpace(Settings.Instance.LastSaveDirectory)
            ? Path.Combine(Directory.GetCurrentDirectory(), "TAS Files")
            : Settings.Instance.LastSaveDirectory;
        
        var dir = Editor.Document.FilePath == Document.ScratchFile
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
            if (CommunicationWrapper.GetConsoleCommand(simple: true) is var simpleConsoleCommand && !string.IsNullOrWhiteSpace(simpleConsoleCommand)) {
                initText += $"{Document.NewLine}{simpleConsoleCommand}{Document.NewLine}   1{Document.NewLine}";
                if (CommunicationWrapper.GetModURL() is var modUrl && !string.IsNullOrWhiteSpace(modUrl)) {
                    initText = modUrl + initText;
                }
            }
        }
        initText += $"{Document.NewLine}#Start{Document.NewLine}";
        
        File.WriteAllText(Document.ScratchFile, initText);
        OpenFile(Document.ScratchFile);
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
    
    public void OpenFile(string filePath) {
        if (filePath == Editor.Document.FilePath && filePath != Document.ScratchFile)
            return;
        
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            Settings.Instance.AddRecentFile(filePath);
        
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
        
        if (filePath != Document.ScratchFile) {
            Settings.Instance.LastSaveDirectory = Path.GetDirectoryName(filePath)!;
        }
        
        void UpdateTitle(Document _0, int _1, int _2) {
            Title = TitleBarText;
        }
    }
    
    private void OnSaveFile() {
        if (Editor.Document.FilePath == Document.ScratchFile) {
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
        
        // Remove scratch file from recent files
        if (Settings.Instance.RecentFiles.FirstOrDefault() == Document.ScratchFile) {
            Settings.Instance.RecentFiles.RemoveAt(0);
        }
        
        File.WriteAllText(filePath, Editor.Document.Text);
        OpenFile(filePath);
    }
    
    private MenuBar CreateMenu() {
        const int minDecimals = 2;
        const int maxDecimals = 12;
        const int minFastForwardSpeed = 2;
        const int maxFastForwardSpeed = 30;
        const float minSlowForwardSpeed = 0.1f;
        const float maxSlowForwardSpeed = 0.9f;
        
        var recordTasButton = MenuUtils.CreateAction("&Record TAS...", Keys.None, () => {
            if (!CommunicationWrapper.Connected) {
                MessageBox.Show("This feature requires the support of the CelesteTAS mod, please launch the game.", MessageBoxButtons.OK);
                return;
            }
            
            RecordDialog.Show();
        });
        recordTasButton.Enabled = CommunicationWrapper.Connected;
        
        // NOTE: Index 0 is the recent files is the current file, so that is skipped
        var openPreviousFile = MenuUtils.CreateAction("Open &Previous File", Application.Instance.AlternateModifier | Keys.Left, () => {
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
        
        var aboutDialog = new AboutDialog {
            ProgramName = "Celeste Studio",
            ProgramDescription = "Editor for editing Celeste TASes with various useful features.",
            Version = Version.ToString(3),
            Website = new Uri("https://github.com/EverestAPI/CelesteTAS-EverestInterop"),
            
            Developers = ["psyGamer", "DemoJameson", "EuniverseCat", "Samah"],
            License = "MIT License",
            Logo = Icon,
        };
            
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
                    recordTasButton,
                }},
                new SubMenuItem {Text = "&Settings", Items = {
                    MenuUtils.CreateSettingToggle("&Send Inputs to Celeste", nameof(Settings.SendInputsToCeleste), Application.Instance.CommonModifier | Keys.D, enabled => {
                        Editor.ShowToastMessage($"{(enabled ? "Enabled" : "Disabled")} Sending Inputs to Celeste", Editor.DefaultToastTime);
                    }),
                    new SubMenuItem {Text = "Automatic Backups", Items = {
                        MenuUtils.CreateSettingToggle("Enabled", nameof(Settings.AutoBackupEnabled)),
                        MenuUtils.CreateSettingNumberInput("Backup Rate (minutes)", nameof(Settings.AutoBackupRate), 0, int.MaxValue, 1),
                        MenuUtils.CreateSettingNumberInput("Backup File Count", nameof(Settings.AutoBackupCount), 0, int.MaxValue, 1),
                    }},
                    MenuUtils.CreateAction("Snippets...", Keys.None, SnippetDialog.Show),
                    MenuUtils.CreateAction("Font...", Keys.None, FontDialog.Show),
                    CreateSettingTheme(),
                    MenuUtils.CreateAction("Open Settings File...", Keys.None, () => ProcessHelper.OpenInDefaultApp(Settings.SettingsPath)),
                }},
                new SubMenuItem { Text = "&Preferences", Items = {
                    MenuUtils.CreateSettingToggle("&Auto Save File", nameof(Settings.AutoSave)),
                    MenuUtils.CreateSettingToggle("Auto Remove Mutually Exclusive Actions", nameof(Settings.AutoRemoveMutuallyExclusiveActions)),
                    MenuUtils.CreateSettingToggle("Always on Top", nameof(Settings.AlwaysOnTop)),
                    MenuUtils.CreateSettingNumberInput<int>("Max Unfolded Lines", nameof(Settings.MaxUnfoldedLines), 0, int.MaxValue, 1),
                    MenuUtils.CreateSettingEnum<InsertDirection>("Insert Direction", nameof(Settings.InsertDirection), ["Above Current Line", "Below Current Line"]),
                    MenuUtils.CreateSettingEnum<CaretInsertPosition>("Caret Insert Position", nameof(Settings.CaretInsertPosition), ["After Inserted Text", "Keep at Previous Position"]),
                    MenuUtils.CreateSettingEnum<CommandSeparator>("Command Separator", nameof(Settings.CommandSeparator), ["Space (\" \")", "Comma (\",\")", "Space + Comma (\", \")"]),
                }},
                new SubMenuItem { Text = "&View", Items = {
                    MenuUtils.CreateSettingToggle("Show Game Info", nameof(Settings.ShowGameInfo)),
                    MenuUtils.CreateSettingToggle("Word Wrap Comments", nameof(Settings.WordWrapComments)),
                    MenuUtils.CreateSettingToggle("Show Fold Indicators", nameof(Settings.ShowFoldIndicators)),
                }},
                new SubMenuItem {Text = "&Game Settings", Enabled = CommunicationWrapper.Connected, Items = {
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
                new SubMenuItem { Text = "&Tools", Items = {
                    MenuUtils.CreateAction("Jadderline", Keys.None, () => {
                        jadderlineForm ??= new();
                        jadderlineForm.Show();
                        jadderlineForm.Closed += (_, _) => jadderlineForm = null;
                    }),
                }},
            },
            ApplicationItems = {
                // application (OS X) or file menu (others)
            },
            QuitItem = MenuUtils.CreateAction("Quit", Keys.None, Application.Instance.Quit),
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => aboutDialog.ShowDialog(this)),
            IncludeSystemItems = MenuBarSystemItems.None,
        };
        
        // The "About" is automatically inserted
        menu.HelpItems.Insert(0, MenuUtils.CreateAction("Home", Keys.None, () => ProcessHelper.OpenInDefaultApp("https://github.com/EverestAPI/CelesteTAS-EverestInterop")));
        
        return menu;
    }

    private MenuItem CreateSettingTheme() {
        var selector = new SubMenuItem { Text = "&Theme" };

        var edit = MenuUtils.CreateAction("&Edit Theme...", Keys.None, () => {
            themeEditorForm ??= new();
            themeEditorForm.Show();
            themeEditorForm.Closed += (_, _) => {
                themeEditorForm = null;
            };
        });

        CreateSettingThemeEntries(selector.Items, edit);
        Settings.ThemeChanged += () => CreateSettingThemeEntries(selector.Items, edit);

        return selector;
    }

    private static void CreateSettingThemeEntries(MenuItemCollection items, MenuItem edit) {
        items.Clear();
        items.Add(edit);

        // The controller is just the first radio button
        RadioMenuItem? controller = null;

        foreach (var name in Theme.BuiltinThemes.Keys.Concat(Settings.Instance.CustomThemes.Keys)) {
            var item = new RadioMenuItem(controller) { Text = name };
            item.Click += (_, _) => Settings.Instance.ThemeName = name;
            item.Checked = Settings.Instance.ThemeName == name;
            
            controller ??= item;
            items.Add(item);
        }
    }
}