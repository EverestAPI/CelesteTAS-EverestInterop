using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using CelesteStudio.Communication;
using CelesteStudio.Data;
using CelesteStudio.Dialog;
using CelesteStudio.Editing;
using CelesteStudio.Tool;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.Drawing;
using FontDialog = CelesteStudio.Dialog.FontDialog;
using Eto.Forms.ThemedControls;
using StudioCommunication;

namespace CelesteStudio;

public sealed class Studio : Form {
    public static Studio Instance = null!;
    public static Version Version { get; private set; } = null!;
    
    /// Platform-specific callback to handle new windows
    public readonly Action<Window> WindowCreationCallback;
    
    /// Actions which aren't associated with any menu and only invokable by hotkey 
    public MenuItem[] GlobalHotkeys { get; private set; } = [];

    public readonly Editor Editor;
    public readonly GameInfoPanel GameInfoPanel;
    private readonly Scrollable EditorScrollable;
    
    private JadderlineForm? jadderlineForm;
    private FeatherlineForm? featherlineForm;
    private ThemeEditor? themeEditorForm;

    private string TitleBarText => Editor.Document.FilePath == Document.ScratchFile 
        ? $"<Scratch> - Studio v{Version.ToString(3)}" 
        : $"{Editor.Document.FileName}{(Editor.Document.Dirty ? "*" : string.Empty)} - Studio v{Version.ToString(3)}   {Editor.Document.FilePath}";
    
    public Studio(Action<Window> windowCreationCallback) {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version!;
        Icon = Assets.AppIcon;
        MinimumSize = new Size(250, 250);
        
        WindowCreationCallback = windowCreationCallback;
        
#if DEBUG
        MenuEntryExtensions.VerifyData();  
#endif
        
        Settings.Load();
        
        Size = Settings.Instance.LastSize;
        if (!Settings.Instance.LastLocation.IsZero) {
            var lastLocation = Settings.Instance.LastLocation;
            var lastSize = Settings.Instance.LastSize;
            
            // Clamp to screen
            var screen = Screen.FromRectangle(new RectangleF(lastLocation, lastSize));
            if (lastLocation.X < screen.WorkingArea.Left) {
                lastLocation = lastLocation with { X = (int)screen.WorkingArea.Left };
            } else if (lastLocation.X + lastSize.Width > screen.WorkingArea.Right) {
                lastLocation = lastLocation with { X = (int)screen.WorkingArea.Right - lastSize.Width };
            }
            if (lastLocation.Y < screen.WorkingArea.Top) {
                lastLocation = lastLocation with { Y = (int)screen.WorkingArea.Top };
            } else if (lastLocation.Y + lastSize.Height > screen.WorkingArea.Bottom) {
                lastLocation = lastLocation with { Y = (int)screen.WorkingArea.Bottom - lastSize.Height };
            }
            Location = lastLocation;
        }
        
        GlobalHotkeys = CreateGlobalHotkeys();
        
        // Needs to be registered before the editor is created 
        Settings.Changed += ApplySettings;
        Settings.KeyBindingsChanged += () => {
            Menu = CreateMenu();
            GlobalHotkeys = CreateGlobalHotkeys();
        };
        
        // Setup editor
        {
            EditorScrollable = new Scrollable {
                Width = Size.Width,
                Height = Size.Height,
            }.FixBorder();
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
    
    /// Shows the "About" dialog, while accounting for WPF theming
    public static void ShowAboutDialog(AboutDialog about, Window parent) {
        if (Eto.Platform.Instance.IsWpf) {
            var aboutHandler = (ThemedAboutDialogHandler)about.Handler;
            var dialog = aboutHandler.Control;
            dialog.Load += (_, _) => Studio.Instance.WindowCreationCallback(dialog);
            dialog.Shown += (_, _) => dialog.Location = parent.Location + new Point((parent.Width - dialog.Width) / 2, (parent.Height - dialog.Height) / 2);
        }

        about.ShowDialog(parent);
    }
    
    private MenuItem[] CreateGlobalHotkeys() {
        return [
            MenuEntry.Game_Start.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.Start)),
            MenuEntry.Game_Pause.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.Pause)),
            MenuEntry.Game_Restart.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.Restart)),
            MenuEntry.Game_FrameAdvance.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.FrameAdvance)),
        ];
    }

    public void RecalculateLayout() {
        GameInfoPanel.Width = ClientSize.Width;
        EditorScrollable.Size = new Size(
            Math.Max(0, ClientSize.Width), 
            Math.Max(0, ClientSize.Height - GameInfoPanel.Height));
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
        
        var recordTasButton = MenuEntry.File_RecordTAS.ToAction(() => {
            if (!CommunicationWrapper.Connected) {
                MessageBox.Show("This feature requires the support of the CelesteTAS mod, please launch the game.", MessageBoxButtons.OK);
                return;
            }
            
            RecordDialog.Show();
        });
        recordTasButton.Enabled = CommunicationWrapper.Connected;
        
        // NOTE: Index 0 is the recent files is the current file, so that is skipped
        var openPreviousFile = MenuEntry.File_OpenPrevious.ToAction(() => {
            if (!ShouldDiscardChanges()) {
                return;
            }
            
            OpenFile(Settings.Instance.RecentFiles[1]);
        });
        openPreviousFile.Enabled = Settings.Instance.RecentFiles.Count > 1;
        
        var recentFilesMenu = new SubMenuItem { Text = "Open &Recent" };
        for (int i = 1; i < Settings.Instance.RecentFiles.Count; i++) {
            string filePath = Settings.Instance.RecentFiles[i];
            if (filePath == Document.ScratchFile) {
                recentFilesMenu.Items.Add(MenuUtils.CreateAction("<Scratch>", Keys.None, () => OpenFile(filePath)));
            } else {
                recentFilesMenu.Items.Add(MenuUtils.CreateAction(filePath, Keys.None, () => OpenFile(filePath)));
            }
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
            
        MenuItem[] items = [
            new SubMenuItem { Text = "&File", Items = {
                MenuEntry.File_New.ToAction(OnNewFile),
                new SeparatorMenuItem(),
                MenuEntry.File_Open.ToAction(OnOpenFile),
                openPreviousFile,
                recentFilesMenu,
                backupsMenu,
                new SeparatorMenuItem(),
                MenuEntry.File_Save.ToAction(OnSaveFile),
                MenuEntry.File_SaveAs.ToAction(OnSaveFileAs),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("&Integrate Read Files"),
                MenuUtils.CreateAction("&Convert to LibTAS Movie..."),
                new SeparatorMenuItem(),
                recordTasButton,
            }},
            new SubMenuItem {Text = "&Settings", Items = {
                MenuEntry.Settings_SendInputs.ToSettingToggle(nameof(Settings.SendInputsToCeleste), enabled => {
                    Editor.ShowToastMessage($"{(enabled ? "Enabled" : "Disabled")} Sending Inputs to Celeste", Editor.DefaultToastTime);
                }),
                new SubMenuItem {Text = "Automatic Backups", Items = {
                    MenuUtils.CreateSettingToggle("Enabled", nameof(Settings.AutoBackupEnabled)),
                    MenuUtils.CreateSettingNumberInput("Backup Rate (minutes)", nameof(Settings.AutoBackupRate), 0, int.MaxValue, 1),
                    MenuUtils.CreateSettingNumberInput("Backup File Count", nameof(Settings.AutoBackupCount), 0, int.MaxValue, 1),
                }},
                MenuUtils.CreateAction("Key Bindings...", Keys.None, KeyBindingDialog.Show),
                MenuUtils.CreateAction("Snippets...", Keys.None, SnippetDialog.Show),
                MenuUtils.CreateAction("Font...", Keys.None, FontDialog.Show),
                CreateSettingTheme(),
                MenuUtils.CreateAction("Open Settings File...", Keys.None, () => ProcessHelper.OpenInDefaultApp(Settings.SettingsPath)),
            }},
            new SubMenuItem { Text = "&Preferences", Items = {
                MenuUtils.CreateSettingToggle("&Auto Save File", nameof(Settings.AutoSave)),
                MenuUtils.CreateSettingToggle("Auto Remove Mutually Exclusive Actions", nameof(Settings.AutoRemoveMutuallyExclusiveActions)),
                MenuUtils.CreateSettingToggle("Auto-Index Room Labels", nameof(Settings.AutoIndexRoomLabels)),
                MenuUtils.CreateSettingNumberInput("Scroll Speed", nameof(Settings.ScrollSpeed), 0.0f, 30.0f, 1),
                MenuUtils.CreateSettingNumberInput("Max Unfolded Lines", nameof(Settings.MaxUnfoldedLines), 0, int.MaxValue, 1),
                MenuUtils.CreateSettingEnum<InsertDirection>("Insert Direction", nameof(Settings.InsertDirection), ["Above Current Line", "Below Current Line"]),
                MenuUtils.CreateSettingEnum<CaretInsertPosition>("Caret Insert Position", nameof(Settings.CaretInsertPosition), ["After Inserted Text", "Keep at Previous Position"]),
                MenuUtils.CreateSettingEnum<CommandSeparator>("Command Separator", nameof(Settings.CommandSeparator), ["Space (\" \")", "Comma (\",\")", "Space + Comma (\", \")"]),
            }},
            new SubMenuItem { Text = "&View", Items = {
                MenuEntry.View_ShowGameInfo.ToSettingToggle(nameof(Settings.ShowGameInfo)),
                MenuEntry.View_ShowSubpixelIndicator.ToSettingToggle(nameof(Settings.ShowSubpixelIndicator)),
                MenuUtils.CreateSettingNumberInput("Subpixel Indicator Scale", nameof(Settings.SubpixelIndicatorScale), 0.1f, 10.0f, 0.25f),
                new SeparatorMenuItem(),
                MenuEntry.View_AlwaysOnTop.ToSettingToggle(nameof(Settings.AlwaysOnTop)),
                MenuEntry.View_WrapComments.ToSettingToggle(nameof(Settings.WordWrapComments)),
                MenuEntry.View_ShowFoldingIndicator.ToSettingToggle(nameof(Settings.ShowFoldIndicators)),
                MenuUtils.CreateSettingEnum<LineNumberAlignment>("Line Number Alignment", nameof(Settings.LineNumberAlignment), ["Left", "Right"]),
                MenuUtils.CreateSettingToggle("Compact Menu Bar", nameof(Settings.CompactMenuBar)),
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
                MenuUtils.CreateAction("Featherline", Keys.None, () => {
                    featherlineForm ??= new();
                    featherlineForm.Show();
                    featherlineForm.Closed += (_, _) => featherlineForm = null;
                }),
            }},
        ];
        
        var quitItem = MenuEntry.File_Quit.ToAction(Application.Instance.Quit);
        var homeItem = MenuUtils.CreateAction("Home", Keys.None, () => ProcessHelper.OpenInDefaultApp("https://github.com/EverestAPI/CelesteTAS-EverestInterop"));
        var aboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
            ShowAboutDialog(new AboutDialog {
                ProgramName = "Celeste Studio",
                ProgramDescription = "Editor for editing Celeste TASes with various useful features.",
                Version = Version.ToString(3),
                Website = new Uri("https://github.com/EverestAPI/CelesteTAS-EverestInterop"),
                
                Developers = ["psyGamer", "DemoJameson", "EuniverseCat", "Samah"],
                License = "MIT License",
                Logo = Icon,
            }, this);
        });
        
        var menu = new MenuBar {
            ApplicationItems = {
                // application (OS X) or file menu (others)
            },
            IncludeSystemItems = MenuBarSystemItems.None,
        };
        
        if (Settings.Instance.CompactMenuBar) {
            // Collapse all entries into a single "Studio" entries
            var studioMenu = new SubMenuItem { Text = "&Studio" };
            studioMenu.Items.AddRange(items);
            studioMenu.Items.Add(new SubMenuItem { Text = "&Help", Items = { homeItem, aboutItem }});
            studioMenu.Items.Add(new SeparatorMenuItem());
            studioMenu.Items.Add(quitItem);
            
            menu.Items.Add(studioMenu);
        } else {
            menu.Items.AddRange(items);
            
            menu.QuitItem = quitItem;
            menu.HelpItems.Add(homeItem);
            menu.AboutItem = aboutItem;
        }
        
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