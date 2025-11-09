using CelesteStudio.Communication;
using Eto.Forms;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CelesteStudio.Editing.AutoCompletion;

public class CommandAutoCompleteMenu : AutoCompleteMenu {
    /// Auto-complete entries for commands and snippets
    private readonly List<Entry> baseEntries = [];

    /// Cancellation token for fetching command-argument auto-complete entries
    private CancellationTokenSource? tokenSource;
    /// Index of the currently fetched entries, to prevent flashing "Loading..." while typing
    private int currArgumentIndex = -1;

    public CommandAutoCompleteMenu(TextEditor editor) : base(editor) {
        // Commands
        CommunicationWrapper.CommandsChanged += _ => GenerateBaseAutoCompleteEntries();
        Settings.Changed += GenerateBaseAutoCompleteEntries;
        GenerateBaseAutoCompleteEntries();

        Shown += (_, _) => {
            // Reset state
            Entries.Clear();
            currArgumentIndex = -1;
        };
    }

    private void GenerateBaseAutoCompleteEntries() {
        baseEntries.Clear();

        // Snippets
        foreach (var snippet in Settings.Instance.Snippets) {
            if (!string.IsNullOrWhiteSpace(snippet.Shortcut) && snippet.Enabled &&
                CreateEntry(snippet.Shortcut, snippet.Insert, "Snippet", hasArguments: false) is { } entry)
            {
                baseEntries.Add(entry);
            }
        }

        // Commands
        foreach (string? commandName in CommandInfo.CommandOrder) {
            if (commandName != null && CommunicationWrapper.Commands.FirstOrDefault(cmd => cmd.Name == commandName) is var command && !string.IsNullOrEmpty(command.Name) &&
                CreateEntry(command.Name, command.Insert.Replace(CommandInfo.Separator, Settings.Instance.CommandSeparatorText), "Command", command.HasArguments) is { } entry)
            {
                baseEntries.Add(entry);
            }
        }
        foreach (var command in CommunicationWrapper.Commands) {
            if (!CommandInfo.CommandOrder.Contains(command.Name) && !CommandInfo.HiddenCommands.Contains(command.Name) &&
                CreateEntry(command.Name, command.Insert.Replace(CommandInfo.Separator, Settings.Instance.CommandSeparatorText), "Command", command.HasArguments) is { } entry)
            {
                baseEntries.Add(entry);
            }
        }

        return;

        Entry? CreateEntry(string name, string insert, string extra, bool hasArguments) {
            if (editor.CreateQuickEditAction(insert, hasArguments) is not { } action) {
                return null;
            }

            return new Entry {
                SearchText = name,
                DisplayText = name,
                ExtraText = extra,
                OnUse = action,
                StorageKey = RootStorageKey,
            };
        }
    }

    private const string StorageKeySeparator = "__";
    private const string RootStorageKey = $"AutoCompleteCommand{StorageKeySeparator}Root";
    private const string BaseStorageKey = $"AutoCompleteCommand{StorageKeySeparator}Command";

    public override void Refresh(bool open = true) {
        string fullLine = Document.Lines[Document.Caret.Row];
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, fullLine.Length);

        // Don't auto-complete on comments or action lines
        string fullLineTrimmed = fullLine.TrimStart();
        if (fullLineTrimmed.StartsWith('#') || fullLineTrimmed.StartsWith('*') || ActionLine.TryParse(fullLineTrimmed, out _)) {
            editor.ClosePopupMenu();
            return;
        }

        // Ignore text to the right of the caret to auto-completion
        string line = fullLine[..Document.Caret.Col].TrimStart();

        if (open) {
            editor.OpenPopupMenu(this);
        }

        // Use auto-complete entries for current command
        var commandLine = CommandLine.Parse(line);
        var fullCommandLine = CommandLine.Parse(fullLine);

        if (commandLine == null || commandLine.Value.Arguments.Length == 0 ||
            fullCommandLine == null || fullCommandLine.Value.Arguments.Length == 0)
        {
            Entries.Clear();
            Entries.AddRange(baseEntries);
            Filter = line;

            currArgumentIndex = -1;
        } else {
            var command = CommunicationWrapper.Commands.FirstOrDefault(cmd => string.Equals(cmd.Name, commandLine.Value.Command, StringComparison.OrdinalIgnoreCase));

            var loadingEntry = new Entry {
                DisplayText = string.Empty,
                SearchText = string.Empty,
                ExtraText = string.Empty,
                OnUse = null!,
                Disabled = true,
            };

            if (!string.IsNullOrEmpty(command.Name) && command.HasArguments) {
                var lastArgRegion = commandLine.Value.Regions[^1];

                tokenSource?.Cancel();
                tokenSource?.Dispose();
                tokenSource = new CancellationTokenSource();

                // Don't clear on same argument to prevent flashing "Loading..."
                bool isNewArgumentIndex = currArgumentIndex != commandLine.Value.Arguments.Length;
                if (isNewArgumentIndex) {
                    Entries.Clear();
                    Entries.Add(loadingEntry);
                    editor.RecalcPopupMenu();
                }
                currArgumentIndex = commandLine.Value.Arguments.Length;

                var token = tokenSource.Token;
                Task.Run(async () => {
                    try {
                        int loadingDots = 0;

                        while (!token.IsCancellationRequested && await Application.Instance.InvokeAsync(() => Visible).ConfigureAwait(false)) {
                            if (!CommunicationWrapper.Connected) {
                                loadingEntry.DisplayText = "Connection with Celeste required for command auto-complete!";
                                await Application.Instance.InvokeAsync(() => {
                                    Entries.Clear();
                                    Entries.Add(loadingEntry);
                                    editor.RecalcPopupMenu();
                                }).ConfigureAwait(false);

                                break;
                            }

                            loadingDots = (loadingDots + 1).Mod(4);
                            loadingEntry.DisplayText = $"Loading{new string('.', loadingDots)}{new string(' ', 3 - loadingDots)}";

                            (var commandEntries, bool done) = await CommunicationWrapper.RequestAutoCompleteEntries(command.Name, commandLine.Value.Arguments, Document.FilePath, Document.Caret.Row, refresh: isNewArgumentIndex).ConfigureAwait(false);
                            isNewArgumentIndex = false; // Clear flag to avoid re-requesting every loop iterator

                            var menuEntries = commandEntries.Select(entry => new Entry {
                                SearchText = entry.Prefix + entry.Name,
                                DisplayText = entry.Name,
                                ExtraText = entry.Extra,
                                Suggestion = entry.Suggestion,
                                StorageKey = entry.StorageKey == null ? null : $"{BaseStorageKey}{StorageKeySeparator}{entry.StorageKey}",
                                StorageName = entry.StorageName,
                                OnUse = () => {
                                    using var __ = Document.Update();

                                    string insert = entry.FullName.Replace(CommandInfo.Separator, Settings.Instance.CommandSeparatorText);

                                    var selectedQuickEdit = editor.GetQuickEdits()
                                        .FirstOrDefault(anchor => Document.Caret.Row == anchor.Row &&
                                                                  Document.Caret.Col >= anchor.MinCol &&
                                                                  Document.Caret.Col <= anchor.MaxCol);

                                    // Jump to the next parameter and open the auto-complete menu if applicable
                                    if (selectedQuickEdit != null) {
                                        // Replace the current quick-edit instead
                                        Document.ReplaceRangeInLine(selectedQuickEdit.Row, selectedQuickEdit.MinCol, selectedQuickEdit.MaxCol, insert);

                                        if (entry.IsDone) {
                                            var quickEdits = editor.GetQuickEdits().ToArray();
                                            bool lastQuickEditSelected = quickEdits.Length != 0 &&
                                                                         quickEdits[^1].Row == Document.Caret.Row &&
                                                                         quickEdits[^1].MinCol <= Document.Caret.Col &&
                                                                         quickEdits[^1].MaxCol >= Document.Caret.Col;

                                            if (lastQuickEditSelected) {
                                                editor.ClearQuickEdits();
                                                Document.Selection.Clear();
                                                Document.Caret.Col = Document.Lines[Document.Caret.Row].Length;

                                                editor.ClosePopupMenu();
                                            } else {
                                                editor.SelectNextQuickEdit();

                                                // Don't start a new base auto-complete. Only arguments
                                                if (!string.IsNullOrWhiteSpace(Document.Lines[Document.Caret.Row])) {
                                                    Refresh();
                                                } else {
                                                    editor.ClosePopupMenu();
                                                }
                                            }
                                        } else {
                                            Document.Selection.Clear();
                                            Document.Caret.Col = selectedQuickEdit.MinCol + insert.Length;

                                            Refresh();
                                        }
                                    } else {
                                        if (!entry.IsDone) {
                                            Document.ReplaceRangeInLine(Document.Caret.Row, lastArgRegion.StartCol, lastArgRegion.EndCol, insert);
                                            Document.Caret.Col = editor.DesiredVisualCol = lastArgRegion.StartCol + insert.Length;
                                            Document.Selection.Clear();

                                            Refresh();
                                        } else if (entry.HasNext ?? false/*command.Value.AutoCompleteEntries.Length != allArgs.Length - 1*/) {
                                            // Include separator for next argument
                                            Document.ReplaceRangeInLine(Document.Caret.Row, lastArgRegion.StartCol, lastArgRegion.EndCol, insert + commandLine.Value.ArgumentSeparator);
                                            Document.Caret.Col = editor.DesiredVisualCol = lastArgRegion.StartCol + insert.Length + commandLine.Value.ArgumentSeparator.Length;
                                            Document.Selection.Clear();

                                            Refresh();
                                        } else {
                                            Document.ReplaceRangeInLine(Document.Caret.Row, lastArgRegion.StartCol, lastArgRegion.EndCol, insert);
                                            Document.Caret.Col = editor.DesiredVisualCol = lastArgRegion.StartCol + insert.Length;
                                            Document.Selection.Clear();

                                            editor.ClosePopupMenu();
                                        }
                                    }
                                },
                            });

                            await Application.Instance.InvokeAsync(() => {
                                Entries.Clear();
                                Entries.AddRange(menuEntries);
                                if (!done) {
                                    Entries.Add(loadingEntry);
                                }

                                editor.RecalcPopupMenu();
                            }).ConfigureAwait(false);

                            if (done) {
                                tokenSource?.Dispose();
                                tokenSource = null;
                                break;
                            }

                            await Task.Delay(TimeSpan.FromSeconds(0.25f), token).ConfigureAwait(false);
                        }
                    } catch (Exception ex) {
                        if (ex is TaskCanceledException) {
                            return; // Ignore
                        }

                        await Console.Error.WriteLineAsync("An unexpected exception occured while trying to load auto-complete entries:");
                        await Console.Error.WriteLineAsync(ex.ToString());

                        await Application.Instance.InvokeAsync(() => {
                            Entries.Clear();
                            Entries.Add(new Entry {
                                DisplayText = "An unexpected exception occured! Please report this issue!",
                                SearchText = string.Empty,
                                ExtraText = string.Empty,
                                OnUse = null!,
                                Disabled = true,
                            });
                            foreach (var exLine in ex.ToString().AsSpan().EnumerateLines()) {
                                Entries.Add(new Entry {
                                    DisplayText = "    " + exLine.ToString(),
                                    SearchText = string.Empty,
                                    ExtraText = string.Empty,
                                    OnUse = null!,
                                    Disabled = true,
                                });
                            }
                            editor.RecalcPopupMenu();
                        });
                    }
                }, token);
            } else {
                Entries.Clear();
                editor.RecalcPopupMenu();
            }

            if (editor.GetSelectedQuickEdit() is { } quickEdit && commandLine.Value.Arguments[^1] == quickEdit.DefaultText) {
                // Display all entries when quick-edit still contains the default
                Filter = string.Empty;
            } else {
                Filter = commandLine.Value.Arguments[^1];
            }
        }
    }
}
