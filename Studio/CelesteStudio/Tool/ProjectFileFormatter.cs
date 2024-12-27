using CelesteStudio.Communication;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CelesteStudio.Tool;

public class ProjectFileFormatterDialog : Eto.Forms.Dialog {
    private const string Version = "1.0.0";

    private readonly Button projectRootButton;
    private string projectRoot;

    private readonly CheckBox editRoomIndices;
    private readonly CheckBox editCommands;

    private readonly NumericStepper startingIndex;
    private readonly DropDown roomIndexType;

    private readonly CheckBox forceCorrectCasing;
    private readonly TextBox argumentSeparator;

    private ProjectFileFormatterDialog() {
        Title = $"Project File Formatter v{Version}";
        Icon = Assets.AppIcon;

        Menu = new MenuBar {
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
                Studio.ShowAboutDialog(new AboutDialog {
                    ProgramName = "Project File Formatter",
                    ProgramDescription = "Applies various formatting choices to all files in the specified project",
                    Version = Version,

                    Developers = ["psyGamer"],
                    Logo = Icon,
                }, this);
            }),
        };

        const int rowWidth = 200;

        // General config
        projectRoot = Editor.FindProjectRoot(Studio.Instance.Editor.Document.FilePath);
        projectRootButton = new Button { Text = projectRoot, Width = 200 };
        projectRootButton.Click += (_, _) => {
            var dialog = new SelectFolderDialog() {
                Title = "Select project root folder",
                Directory = projectRoot
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok) {
                projectRoot = dialog.Directory;
                projectRootButton.Text = projectRoot;
            }
        };

        // Auto-room-indexing
        editRoomIndices = new CheckBox { Width = rowWidth, Checked = true };
        startingIndex = new NumericStepper { MinValue = 0, DecimalPlaces = 0, Width = rowWidth, Value = StyleConfig.Current.RoomLabelStartingIndex ?? 0 };
        roomIndexType = new DropDown {
            Items = {
                new ListItem { Text = "Only current File", Key = nameof(AutoRoomIndexing.CurrentFile) },
                new ListItem { Text = "Including Read-commands", Key = nameof(AutoRoomIndexing.IncludeReads) },
            },
            SelectedKey = StyleConfig.Current.RoomLabelIndexing?.ToString() ?? nameof(AutoRoomIndexing.CurrentFile),
            Width = rowWidth
        };

        // Command formatting
        editCommands = new CheckBox { Width = rowWidth, Checked = true };
        forceCorrectCasing = new CheckBox { Width = rowWidth, Checked = true };
        argumentSeparator = new TextBox { Width = rowWidth, Text = StyleConfig.Current.CommandArgumentSeparator ?? ", " };

        var autoRoomIndexingLayout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        {
            autoRoomIndexingLayout.BeginVertical();
            autoRoomIndexingLayout.BeginHorizontal();

            autoRoomIndexingLayout.AddCentered(new Label { Text = "Starting Index" });
            autoRoomIndexingLayout.Add(startingIndex);

            autoRoomIndexingLayout.EndBeginHorizontal();

            autoRoomIndexingLayout.AddCentered(new Label { Text = "Room Indexing Type" });
            autoRoomIndexingLayout.Add(roomIndexType);
        }
        var commandLayout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        {
            commandLayout.BeginVertical();
            commandLayout.BeginHorizontal();

            commandLayout.AddCentered(new Label { Text = "Force Correct Casing" });
            commandLayout.Add(forceCorrectCasing);

            commandLayout.EndBeginHorizontal();

            commandLayout.AddCentered(new Label { Text = "Argument Separator" });
            commandLayout.Add(argumentSeparator);
        }

        DefaultButton = new Button((_, _) => Format()) { Text = "&Format" };
        AbortButton = new Button((_, _) => Close()) { Text = "&Cancel" };

        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);

        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Items = {
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { new Label { Text = "Select Project Root Folder" }, projectRootButton }
                },

                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { new Label { Text = "Format Room Label Indices" }, editRoomIndices }
                },
                // NOTE: The only reason Scrollables are used, is because they provide a border
                new Scrollable { Content = autoRoomIndexingLayout, Padding = 5 }.FixBorder(),

                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { new Label { Text = "Format Commands" }, editCommands }
                },
                new Scrollable { Content = commandLayout, Padding = 5 }.FixBorder(),
            }
        };
        Resizable = false;

        Studio.RegisterDialog(this);
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }

    private void Format() {
        string[] files = Directory.GetFiles(projectRoot, "*.tas", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden });

        bool formatRoomIndices = Application.Instance.Invoke(() => editRoomIndices.Checked == true);
        bool formatCommands = Application.Instance.Invoke(() => editCommands.Checked == true);

        bool includeReads = Application.Instance.Invoke(() => roomIndexType.SelectedKey == nameof(AutoRoomIndexing.IncludeReads));
        int startIndex = (int)Application.Instance.Invoke(() => startingIndex.Value);

        bool forceCase = Application.Instance.Invoke(() => forceCorrectCasing.Checked == true);
        string separator = Application.Instance.Invoke(() => argumentSeparator.Text);

        int totalTasks = 0, finishedTasks = 0;

        Label progressLabel;
        ProgressBar progressBar;
        Button doneButton;

        var progressPopup = new Eto.Forms.Dialog {
            Title = "Processing...",
            Icon = Assets.AppIcon,

            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = {
                    (progressLabel = new Label { Text = $"Formatting files {finishedTasks} / {totalTasks}..." }),
                    (progressBar = new ProgressBar { Width = 300 }),
                    (doneButton = new Button { Text = "Done", Enabled = false }),
                },
            },

            Resizable = false,
            Closeable = false,
            ShowInTaskbar = true,
        };
        doneButton.Click += (_, _) => {
            progressPopup.Close();
            Close();
        };

        Studio.RegisterDialog(progressPopup);
        progressPopup.Load += (_, _) => Studio.Instance.WindowCreationCallback(progressPopup);
        progressPopup.Shown += (_, _) => progressPopup.Location = Location + new Point((Width - progressPopup.Width) / 2, (Height - progressPopup.Height) / 2);

        foreach (string file in files) {
            if (Directory.Exists(file)) {
                continue;
            }

            totalTasks++;
            Task.Run(async () => {
                Console.WriteLine($"Reformatting '{file}'...");

                try {
                    if (formatRoomIndices) {
                        await UpdateRoomLabelIndices(file, startIndex, includeReads).ConfigureAwait(false);
                    }
                    if (formatCommands) {
                        await UpdateCommands(file, forceCase, separator).ConfigureAwait(false);
                    }

                    Console.WriteLine($"Successfully reformatted '{file}'");
                } catch (Exception ex) {
                    Console.WriteLine($"Failed reformatted '{file}': {ex}");
                }

                finishedTasks++;
                await Application.Instance.InvokeAsync(UpdateProgress).ConfigureAwait(false);
            });
        }

        UpdateProgress();
        progressPopup.ShowModal();

        void UpdateProgress() {
            progressLabel.Text = finishedTasks == totalTasks
                ? $"Successfully formatted {progressBar.MaxValue} files."
                : $"Formatting files {progressBar.Value} / {progressBar.MaxValue}...";
            progressBar.Value = finishedTasks;
            progressBar.MaxValue = totalTasks;

            if (finishedTasks == totalTasks) {
                doneButton.Enabled = true;
                progressPopup.Title = "Complete";
            }
        }
    }

    // Mostly copied from Editor
    private async Task UpdateRoomLabelIndices(string filePath, int startIndex, bool includeReads) {
        var editor = Studio.Instance.Editor;

        // room label without indexing -> lines of all occurrences
        Dictionary<string, List<(int Row, bool Update)>> roomLabels = [];

        foreach ((string line, int row, string file, _) in editor.IterateDocumentLines(includeReads, filePath)) {
            var match = Editor.RoomLabelRegex.Match(line);
            if (!match.Success) {
                continue;
            }

            bool isCurrentFile = file == filePath;
            string label = match.Groups[1].Value.Trim();

            if (roomLabels.TryGetValue(label, out var list)) {
                list.Add((row, isCurrentFile));
            } else {
                roomLabels[label] = [(row, isCurrentFile)];
            }
        }

        string[]? lines = null;
        if (filePath != editor.Document.FilePath) {
            await editor.RefactorSemaphore.WaitAsync().ConfigureAwait(false);
            if (!Editor.FileCache.TryGetValue(filePath, out lines)) {
                Editor.FileCache[filePath] = lines = await File.ReadAllLinesAsync(filePath).ConfigureAwait(false);
            }
            editor.RefactorSemaphore.Release();
        }

        using var __ = editor.Document.Update(raiseEvents: false);
        foreach ((string label, var occurrences) in roomLabels) {
            if (occurrences.Count == 1) {
                if (!occurrences[0].Update) {
                    continue;
                }

                if (filePath == editor.Document.FilePath) {
                    await editor.RefactorLabelName(editor.Document.Lines[occurrences[0].Row]["#".Length..], $"lvl_{label}", filePath).ConfigureAwait(false);
                    editor.Document.ReplaceLine(occurrences[0].Row, $"#lvl_{label}");
                } else {
                    await editor.RefactorLabelName(lines![occurrences[0].Row]["#".Length..], $"lvl_{label}", filePath).ConfigureAwait(false);
                    lines[occurrences[0].Row] = $"#lvl_{label}";
                }

                continue;
            }

            for (int i = 0; i < occurrences.Count; i++) {
                if (!occurrences[i].Update) {
                    continue;
                }

                if (filePath == editor.Document.FilePath) {
                    await editor.RefactorLabelName(editor.Document.Lines[occurrences[i].Row]["#".Length..], $"lvl_{label} ({i + startIndex})", filePath).ConfigureAwait(false);
                    editor.Document.ReplaceLine(occurrences[i].Row, $"#lvl_{label} ({i + startIndex})");
                } else {
                    await editor.RefactorLabelName(lines![occurrences[i].Row]["#".Length..], $"lvl_{label} ({i + startIndex})", filePath).ConfigureAwait(false);
                    lines[occurrences[i].Row] = $"#lvl_{label} ({i + startIndex})";
                }
            }
        }

        if (filePath != editor.Document.FilePath) {
            await Studio.Instance.Editor.RefactorSemaphore.WaitAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(filePath, Document.FormatLinesToText(lines!)).ConfigureAwait(false);
            Studio.Instance.Editor.RefactorSemaphore.Release();
        }
    }

    private async Task UpdateCommands(string filePath, bool forceCase, string separator) {
        var editor = Studio.Instance.Editor;

        if (filePath == editor.Document.FilePath) {
            using var __ = editor.Document.Update(raiseEvents: false);

            for (int row = 0; row < editor.Document.Lines.Count; row++) {
                if (!CommandLine.TryParse(editor.Document.Lines[row], out var commandLine)) {
                    continue;
                }

                string commandName;
                if (forceCase && CommunicationWrapper.Commands.FirstOrDefault(cmd => string.Equals(cmd.Name, commandLine.Command, StringComparison.OrdinalIgnoreCase)) is var command && !string.IsNullOrEmpty(command.Name)) {
                    commandName = command.Name;
                } else {
                    commandName = commandLine.Command;
                }

                editor.Document.ReplaceLine(row, string.Join(separator, [commandName, ..commandLine.Arguments]));
            }
        } else {
            await editor.RefactorSemaphore.WaitAsync().ConfigureAwait(false);
            if (!Editor.FileCache.TryGetValue(filePath, out string[]? lines)) {
                Editor.FileCache[filePath] = lines = await File.ReadAllLinesAsync(filePath).ConfigureAwait(false);
            }
            editor.RefactorSemaphore.Release();

            for (int row = 0; row < lines.Length; row++) {
                if (!CommandLine.TryParse(lines[row], out var commandLine)) {
                    continue;
                }

                string commandName;
                if (forceCase && CommunicationWrapper.Commands.FirstOrDefault(cmd => string.Equals(cmd.Name, commandLine.Command, StringComparison.OrdinalIgnoreCase)) is var command && !string.IsNullOrEmpty(command.Name)) {
                    commandName = command.Name;
                } else {
                    commandName = commandLine.Command;
                }

                lines[row] = string.Join(separator, [commandName, ..commandLine.Arguments]);
            }

            await editor.RefactorSemaphore.WaitAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(filePath, Document.FormatLinesToText(lines)).ConfigureAwait(false);
            editor.RefactorSemaphore.Release();
        }
    }

    public static void Show() => new ProjectFileFormatterDialog().ShowModal();
}
