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
using CelesteStudio.Migration;
using CelesteStudio.Tool;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.Drawing;
using FontDialog = CelesteStudio.Dialog.FontDialog;
using Eto.Forms.ThemedControls;
using StudioCommunication;
using StudioCommunication.Util;
using System.Collections.Generic;

namespace CelesteStudio;

public sealed class Studio : Form {
    public static Studio Instance = null!;
    public static readonly string Version;

    private const string VersionSuffix = "-dev";

    static Studio() {
        Version = $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}{VersionSuffix}";
    }

    /// Event which will be called before Studio exits
    public event Action Exiting = () => { };

    /// Platform-specific callback to handle new windows
    public readonly Action<Window> WindowCreationCallback;

    /// Actions which aren't associated with any menu and only invokable by hotkey
    public MenuItem[] GlobalHotkeys { get; private set; }

    /// Path to the Celeste install or null if it couldn't be found
    public static string? CelesteDirectory { get; private set; }

    /// For some **UNHOLY** reasons, not calling Content.UpdateLayout() in RecalculateLayout() places during startup causes themeing to crash.
    /// _However_, while this hack is active, you can't resize the window, so this has to be disabled again as soon as possible...
    /// I would personally like to burn WPF to the ground ._.
    public bool WPFHackEnabled = true;

    public readonly Editor Editor;
    public readonly GameInfo GameInfo;

    private readonly Scrollable editorScrollable;
    private readonly GameInfoPanel gameInfoPanel;

    private JadderlineForm? jadderlineForm;
    private FeatherlineForm? featherlineForm;
    private ThemeEditor? themeEditorForm;

    private string TitleBarText => Editor.Document.FilePath == Document.ScratchFile
        ? $"<Scratch> - Studio {Version}"
        // Hide username inside title bar
        : $"{Editor.Document.FileName}{(Editor.Document.Dirty ? "*" : string.Empty)} - Studio {Version}   {Editor.Document.FilePath.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~")}";

    /// Size of scroll bars, depending on the current platform
    public static int ScrollBarSize {
        get {
            if (Eto.Platform.Instance.IsWpf) {
                return 17;
            }
            if (Eto.Platform.Instance.IsGtk) {
                return 17; // This probably relies on the GTK theme, but being slight off isn't too big of an issue
            }
            if (Eto.Platform.Instance.IsMac) {
                return 15;
            }
            return 0;
        }
    }

    public Studio(string[] args, Action<Window> windowCreationCallback) {
        Instance = this;
        Icon = Assets.AppIcon;
        MinimumSize = new Size(250, 250);
        AllowDrop = true;

        WindowCreationCallback = windowCreationCallback;

        // Find game directory
        if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Celeste.dll"))) {
            // Windows / Linux
            CelesteDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..");
        }
        else if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Celeste.dll"))) {
            // macOS (inside .app bundle)
            CelesteDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..");
        } else {
            Console.WriteLine("Couldn't find game directory");
        }

        // Close other Studio instances to avoid conflicts
        foreach (var process in Process.GetProcesses().Where(process => process.ProcessName is "CelesteStudio" or "CelesteStudio.WPF" or "CelesteStudio.GTK" or "CelesteStudio.Mac" or "Celeste Studio")) {
            if (process.Id == Process.GetCurrentProcess().Id) {
                continue;
            }

            Console.WriteLine($"Closing process {process.ProcessName} ({process.Id})...");
            process.Terminate();
            process.WaitForExit(TimeSpan.FromSeconds(10.0f));
        }

        // Ensure config directory exists
        if (!Directory.Exists(Settings.BaseConfigPath)) {
            Directory.CreateDirectory(Settings.BaseConfigPath);
        }

        Migrator.ApplyPreLoadMigrations();

#if DEBUG
        MenuEntryExtensions.VerifyData();
#endif

        Settings.Load();

        Migrator.ApplyPostLoadMigrations();

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
        } else {
            // Center window
            Shown += (_, _) => {
                var area = Screen.WorkingArea;
                Location = new Point(
                    (int)(area.MiddleX - Size.Width / 2.0f),
                    (int)(area.MiddleY - Size.Height / 2.0f));
            };
        }

        GlobalHotkeys = CreateGlobalHotkeys();

        // Needs to be registered before the editor is created
        Settings.Changed += ApplySettings;
        Settings.KeyBindingsChanged += () => {
            Menu = CreateMenu();
            GlobalHotkeys = CreateGlobalHotkeys();
        };
        // Reflect changed game-settings
        CommunicationWrapper.SettingsChanged += _ => {
            Menu = CreateMenu();
        };

        // Setup editor
        {
            editorScrollable = new Scrollable {
                Width = Size.Width,
                Height = Size.Height,
            }.FixBorder();
            Editor = new Editor(Document.Dummy, editorScrollable);
            editorScrollable.Content = Editor;

            // On GTK, prevent the scrollable from reacting to Home/End
            if (Eto.Platform.Instance.IsGtk) {
                editorScrollable.KeyDown += (_, e) => e.Handled = true;
            }

            // Needs to be done after the Editor is set up
            GameInfo = new GameInfo();
            gameInfoPanel = new GameInfoPanel();

            Content = new StackLayout {
                Padding = 0,
                Items = {
                    editorScrollable,
                    gameInfoPanel
                }
            };

            Shown += (_, _) => {
                gameInfoPanel.UpdateLayout();
                RecalculateLayout();
            };
            SizeChanged += (_, _) => {
                RecalculateLayout();
            };
            gameInfoPanel.SizeChanged += (_, _) => {
                RecalculateLayout();
            };

            ApplySettings();

            // Only enable some settings while connected
            CommunicationWrapper.ConnectionChanged += () => Application.Instance.Invoke(() => {
                Menu = CreateMenu();
            });
        }

        Load += (_, _) => {
            if (args.Length > 0) {
                OpenFile(args[0]);
            } else if (Settings.Instance.RecentFiles.Count > 0 &&
                       !string.IsNullOrWhiteSpace(Settings.Instance.RecentFiles[0]) &&
                       File.Exists(Settings.Instance.RecentFiles[0]))
            {
                // Re-open last file if possible
                OpenFile(Settings.Instance.RecentFiles[0]);
            } else {
                OnNewFile();
            }
        };

        CommunicationWrapper.Start();
    }

    // Dialogs should always be focused over the editor / tools, but not other OS windows
    public static void RegisterDialog(Eto.Forms.Dialog dialog) {
        // For some reason macOS can just revive dialogs from the dead
        // Thankfully Topmost already behaves how we want on macOS
        if (Eto.Platform.Instance.IsMac) {
            dialog.Topmost = true;
            return;
        }

        Instance.GotFocus += Refocus;
        if (Instance.jadderlineForm != null) {
            Instance.jadderlineForm.GotFocus += Refocus;
        }
        if (Instance.featherlineForm != null) {
            Instance.featherlineForm.GotFocus += Refocus;
        }

        bool wasTopmost = Instance.Topmost;

        // Studio can't be also top-most while a dialog is open, since it would be above the dialog
        Instance.Topmost = false;
        Instance.Focus();

        dialog.Closed += (_, _) => Instance.Topmost = wasTopmost;

        // Allow closing dialog via ESC. Supported by default on GTK, but not WPF
        dialog.KeyDown += (_, e) => {
            if (e.Key == Keys.Escape) {
                e.Handled = true;
                dialog.Close();
            }
        };

        return;

        void Refocus(object? sender, EventArgs eventArgs) => dialog.Focus();
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

    private static MenuItem[] CreateGlobalHotkeys() {
        return [
            MenuEntry.Game_Start.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.Start)),
            MenuEntry.Game_Pause.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.Pause)),
            MenuEntry.Game_Restart.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.Restart)),
            MenuEntry.Game_FrameAdvance.ToAction(() => CommunicationWrapper.SendHotkey(HotkeyID.FrameAdvance)),
        ];
    }

    private void RecalculateLayout() {
        gameInfoPanel.Width = ClientSize.Width;
        editorScrollable.Size = new Size(
            Math.Max(0, ClientSize.Width),
            Math.Max(0, ClientSize.Height - Math.Max(0, gameInfoPanel.Height)));

        // Calling UpdateLayout() seems to be required on GTK but causes issues on WPF
        // TODO: Figure out how macOS handles this
        if (Eto.Platform.Instance.IsGtk) {
            gameInfoPanel.UpdateLayout();
            editorScrollable.UpdateLayout();
            Content.UpdateLayout();
        } else if (Eto.Platform.Instance.IsWpf && WPFHackEnabled) {
            Content.UpdateLayout();
        }
    }

    private void ApplySettings() {
        Topmost = Settings.Instance.AlwaysOnTop;
        Menu = CreateMenu(); // Recreate menu to reflect changes
    }

    protected override void OnDragDrop(DragEventArgs e) {
        if (e.Data.ContainsUris && e.Data.Uris.Length > 0) {
            OpenFile(Uri.UnescapeDataString(e.Data.Uris[0].AbsolutePath));
        }
    }
    protected override void OnDragEnter(DragEventArgs e) {
        e.Effects = DragEffects.Copy;
    }

    protected override void OnClosing(CancelEventArgs e) {
        if (!ShouldDiscardChanges(checkTempFile: false)) {
            e.Cancel = true;
            return;
        }

        if (Settings.Instance.AutoSave) {
            Editor.FixInvalidInputs();
            Editor.Document.Save();
        }

        Settings.Instance.LastLocation = Location;
        // Avoid storing sizes below the minimum in the settings
        if (Size.Width >= MinimumSize.Width && Size.Height >= MinimumSize.Height) {
            Settings.Instance.LastSize = Size;
        }

        Settings.Save();

        CommunicationWrapper.SendPath(string.Empty);
        CommunicationWrapper.Stop();

        Exiting();

        base.OnClosing(e);
    }

    private bool ShouldDiscardChanges(bool checkTempFile = true) {
        bool showConfirmation = Editor.Document.Dirty;

        // Only ask for discarding changes if scratch file actually contains something
        if (checkTempFile && Editor.Document.FilePath == Document.ScratchFile) {
            bool containsInputs = false;
            foreach (string line in Editor.Document.Lines) {
                if (ActionLine.TryParse(line, out var actionLine) && (actionLine.Actions != Actions.None || actionLine.FeatherAngle != null || actionLine.FeatherMagnitude != null)) {
                    containsInputs = true;
                    break;
                }
            }

            showConfirmation |= containsInputs;
        }

        if (showConfirmation) {
            var confirm = MessageBox.Show("""
                                                    You have unsaved changes.
                                                    Are you sure you want to discard them?
                                                    """, MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.No);
            return confirm == DialogResult.Yes;
        }

        return true;
    }

    private string GetFilePickerDirectory() {
        string fallbackDir = string.IsNullOrWhiteSpace(Settings.Instance.LastSaveDirectory)
            ? Path.Combine(CelesteDirectory ?? string.Empty, "TAS Files")
            : Settings.Instance.LastSaveDirectory;

        string dir = Editor.Document.FilePath == Document.ScratchFile
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

        string simpleConsoleCommand = CommunicationWrapper.GetConsoleCommand(simple: true);
        LevelInfo? levelInfo = CommunicationWrapper.GetLevelInfo();

        string initText = $"RecordCount: 1{Document.NewLine}";
        if (levelInfo != null && !string.IsNullOrWhiteSpace(levelInfo?.ModUrl)) {
            initText = levelInfo?.ModUrl + initText;
        }
        if (!string.IsNullOrWhiteSpace(simpleConsoleCommand)) {
            initText += $"{Document.NewLine}{simpleConsoleCommand}{Document.NewLine}   1{Document.NewLine}";
        }
        initText += $"{Document.NewLine}#Start{Document.NewLine}";
        if (levelInfo?.WakeupTime is { } wakeupTime) {
            initText += wakeupTime.ToString().PadLeft(ActionLine.MaxFramesDigits) + Document.NewLine;
        }

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

        void UpdateTitle(Document _0, Dictionary<int, string> _1, Dictionary<int, string> _2) {
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

    private void OnIntegrateReadFiles() {
        var dialog = new SaveFileDialog {
            Filters = { new FileFilter("TAS", ".tas") },
            Directory = new Uri(GetFilePickerDirectory()),
            FileName = Path.GetDirectoryName(Editor.Document.FilePath) + "/" + Path.GetFileNameWithoutExtension(Editor.Document.FilePath) + "_Integrated.tas"
        };
        Console.WriteLine($"f {Editor.Document.FilePath} & {Path.GetDirectoryName(Editor.Document.FilePath) + Path.GetFileNameWithoutExtension(Editor.Document.FilePath) + "_Integrated.tas"}");

        if (dialog.ShowDialog(this) != DialogResult.Ok) {
            return;
        }

        string filePath = dialog.FileName;
        if (Path.GetExtension(filePath) != ".tas") {
            filePath += ".tas";
        }

        IntegrateReadFiles.Generate(Editor.Document.FilePath, filePath);

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

        // Don't display Settings.SendInputsNonWritable on WPF, since it's not supported there
        var inputSendingMenu = new SubMenuItem { Text = "&Input Sending", Items = {
                MenuUtils.CreateSettingToggle("On Inputs", nameof(Settings.SendInputsOnActionLines)),
                MenuUtils.CreateSettingToggle("On Comments", nameof(Settings.SendInputsOnComments)),
                MenuUtils.CreateSettingToggle("On Commands", nameof(Settings.SendInputsOnCommands)),
        }};
        if (!Platform.IsWpf) {
            inputSendingMenu.Items.Add(MenuUtils.CreateSettingToggle("Always send non-writable Inputs", nameof(Settings.SendInputsNonWritable)));
        }
        inputSendingMenu.Items.Add(new SeparatorMenuItem());
        inputSendingMenu.Items.Add(MenuUtils.CreateSettingNumberInput("Typing Timeout", nameof(Settings.SendInputsTypingTimeout), 0.0f, 5.0f, 0.1f));

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
                MenuUtils.CreateAction("&Convert to LibTAS Movie..."),
                new SeparatorMenuItem(),
                recordTasButton,
            }},
            new SubMenuItem {Text = "&Settings", Items = {
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
                MenuUtils.CreateSettingEnum<AutoRoomIndexing>("Auto-Index Room Labels", nameof(Settings.AutoIndexRoomLabels), ["Disabled", "Current File", "Include Read-commands"]),
                MenuUtils.CreateSettingToggle("Auto-Select Full Input line", nameof(Settings.AutoSelectFullActionLine)),
                MenuUtils.CreateSettingToggle("Sync &Caret with Playback", nameof(Settings.SyncCaretWithPlayback)),
                MenuEntry.Settings_SendInputs.ToSettingToggle(nameof(Settings.SendInputsToCeleste), enabled => {
                    Editor.ShowToastMessage($"{(enabled ? "Enabled" : "Disabled")} Sending Inputs to Celeste", Editor.DefaultToastTime);
                }),
                inputSendingMenu,
                MenuUtils.CreateSettingNumberInput("Scroll Speed", nameof(Settings.ScrollSpeed), 0.0f, 30.0f, 1),
                MenuUtils.CreateSettingNumberInput("Max Unfolded Lines", nameof(Settings.MaxUnfoldedLines), 0, int.MaxValue, 1),
                MenuUtils.CreateSettingEnum<InsertDirection>("Insert Direction", nameof(Settings.InsertDirection), ["Above Current Line", "Below Current Line"]),
                MenuUtils.CreateSettingEnum<CaretInsertPosition>("Caret Insert Position", nameof(Settings.CaretInsertPosition), ["After Inserted Text", "Keep at Previous Position"]),
                MenuUtils.CreateSettingEnum<CommandSeparator>("Command Separator", nameof(Settings.CommandSeparator), ["Space (\" \")", "Comma (\",\")", "Space + Comma (\", \")"]),
            }},
            new SubMenuItem { Text = "&View", Items = {
                // TODO: Use MenuEntry.View_ShowGameInfo again
                MenuUtils.CreateSettingEnum<GameInfoType>("Game Info", nameof(Settings.GameInfo), ["Disabled", "Panel", "Popout"]),
                MenuUtils.CreateSettingNumberInput("Maximum Game Info Height", nameof(Settings.MaxGameInfoHeight), 0.1f, 0.9f, 0.05f, percent => $"{percent * 100.0f:F0}%"),
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
                MenuUtils.CreateGameSettingToggle("&Hitboxes", nameof(GameSettings.Hitboxes)),
                MenuUtils.CreateGameSettingToggle("&Trigger Hitboxes", nameof(GameSettings.TriggerHitboxes)),
                MenuUtils.CreateGameSettingToggle("Unloaded Room Hitboxes", nameof(GameSettings.UnloadedRoomsHitboxes)),
                MenuUtils.CreateGameSettingToggle("Camera Hitboxes", nameof(GameSettings.CameraHitboxes)),
                MenuUtils.CreateGameSettingToggle("&Simplified Hitboxes", nameof(GameSettings.SimplifiedHitboxes)),
                MenuUtils.CreateGameSettingEnum<ActualCollideHitboxType>("&Actual Collide Hitboxes", nameof(GameSettings.ActualCollideHitboxes), ["Off", "Override", "Append"]),
                new SeparatorMenuItem(),
                MenuUtils.CreateGameSettingToggle("&Simplified &Graphics", nameof(GameSettings.SimplifiedGraphics)),
                MenuUtils.CreateGameSettingToggle("Game&play", nameof(GameSettings.Gameplay)),
                new SeparatorMenuItem(),
                MenuUtils.CreateGameSettingToggle("&Center Camera", nameof(GameSettings.CenterCamera)),
                MenuUtils.CreateGameSettingToggle("Center Camera Horizontally Only", nameof(GameSettings.CenterCameraHorizontallyOnly)),
                new SeparatorMenuItem(),
                MenuUtils.CreateGameSettingToggle("&Info HUD", nameof(GameSettings.InfoHud)),
                MenuUtils.CreateGameSettingToggle("TAS Input Info", nameof(GameSettings.InfoTasInput)),
                MenuUtils.CreateGameSettingToggle("Game Info", nameof(GameSettings.InfoGame)),
                MenuUtils.CreateGameSettingToggle("Subpixel Indicator", nameof(GameSettings.InfoSubpixelIndicator)),
                MenuUtils.CreateGameSettingEnum<HudOptions>("Custom Info", nameof(GameSettings.InfoCustom), ["Off", "HUD Only", "Studio Only", "Both"]),
                MenuUtils.CreateGameSettingEnum<HudOptions>("Watch Entity Info", nameof(GameSettings.InfoWatchEntity), ["Off", "HUD Only", "Studio Only", "Both"]),
                new SeparatorMenuItem(),
                MenuUtils.CreateGameSettingNumberInput("Position Decimals", nameof(GameSettings.PositionDecimals), minDecimals, maxDecimals, 1),
                MenuUtils.CreateGameSettingNumberInput("Speed Decimals", nameof(GameSettings.SpeedDecimals), minDecimals, maxDecimals, 1),
                MenuUtils.CreateGameSettingNumberInput("Velocity Decimals", nameof(GameSettings.VelocityDecimals), minDecimals, maxDecimals, 1),
                MenuUtils.CreateGameSettingNumberInput("Angle Decimals", nameof(GameSettings.AngleDecimals), minDecimals, maxDecimals, 1),
                MenuUtils.CreateGameSettingNumberInput("Custom Info Decimals", nameof(GameSettings.CustomInfoDecimals), minDecimals, maxDecimals, 1),
                MenuUtils.CreateGameSettingNumberInput("Subpixel Indicator Decimals", nameof(GameSettings.SubpixelIndicatorDecimals), minDecimals, maxDecimals, 1),
                MenuUtils.CreateGameSettingEnum<SpeedUnit>("Speed Unit", nameof(GameSettings.SpeedUnit), ["px/s", "px/f"]),
                MenuUtils.CreateGameSettingEnum<SpeedUnit>("Velocity Unit", nameof(GameSettings.VelocityUnit), ["px/s", "px/f"]),
                new SeparatorMenuItem(),
                MenuUtils.CreateGameSettingNumberInput("Fast Forward Speed", nameof(GameSettings.FastForwardSpeed), minFastForwardSpeed, maxFastForwardSpeed, 1),
                MenuUtils.CreateGameSettingNumberInput("Slow Forward Speed", nameof(GameSettings.SlowForwardSpeed), minSlowForwardSpeed, maxSlowForwardSpeed, 0.1f),
            }},
            new SubMenuItem { Text = "&Tools", Items = {
                MenuUtils.CreateAction("&Project File Formatter", Keys.None, ProjectFileFormatterDialog.Show),
                MenuUtils.CreateAction("&Integrate Read Files", Keys.None, OnIntegrateReadFiles),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("&Jadderline", Keys.None, () => {
                    jadderlineForm ??= new();
                    jadderlineForm.Show();
                    jadderlineForm.Closed += (_, _) => jadderlineForm = null;
                }),
                MenuUtils.CreateAction("&Featherline", Keys.None, () => {
                    featherlineForm ??= new();
                    featherlineForm.Show();
                    featherlineForm.Closed += (_, _) => featherlineForm = null;
                }),
            }},
        ];

        var quitItem = MenuEntry.File_Quit.ToAction(Application.Instance.Quit);
        var homeItem = MenuUtils.CreateAction("Open README...", Keys.None, () => ProcessHelper.OpenInDefaultApp("https://github.com/EverestAPI/CelesteTAS-EverestInterop"));
        var wikiItem = MenuUtils.CreateAction("Open wiki...", Keys.None, () => ProcessHelper.OpenInDefaultApp("https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki"));
        var whatsNewItem = MenuUtils.CreateAction("What's new?", Keys.None, () => {
            var asm = Assembly.GetExecutingAssembly();
            // TODO: Don't hardcode the current changelog
            if (asm.GetManifestResourceStream($"Changelogs/v3.0.0.md") is { } stream) {
                WhatsNewDialog.Show($"What's new in Studio v3.0.0?", new StreamReader(stream).ReadToEnd());
            }
        });
        var aboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
            ShowAboutDialog(new AboutDialog {
                ProgramName = "Celeste Studio",
                ProgramDescription = "Editor for editing Celeste TASes with various useful features.",
                Version = Version,
                Website = new Uri("https://github.com/EverestAPI/CelesteTAS-EverestInterop"),

                Developers = ["psyGamer", "DemoJameson", "EuniverseCat", "dubi steinkek", "Mirkwood"],
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
            studioMenu.Items.Add(new SubMenuItem { Text = "&Help", Items = { homeItem, wikiItem, whatsNewItem, new SeparatorMenuItem(), aboutItem }});
            studioMenu.Items.Add(new SeparatorMenuItem());
            studioMenu.Items.Add(quitItem);

            menu.Items.Add(studioMenu);
        } else {
            menu.Items.AddRange(items);

            menu.QuitItem = quitItem;
            menu.HelpItems.Add(homeItem);
            menu.HelpItems.Add(wikiItem);
            menu.HelpItems.Add(whatsNewItem);
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
