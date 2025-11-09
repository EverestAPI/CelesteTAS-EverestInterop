using CelesteStudio.Communication;
using Eto.Forms;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CelesteStudio.Editing.AutoCompletion;

public class InfoTemplateAutoCompleteMenu : AutoCompleteMenu {
    /// Cancellation token for fetching command-argument auto-complete entries
    private CancellationTokenSource? tokenSource;

    /// Reset once per popup to keep things somewhat up-to-date
    private bool needEntryRefresh = true;

    public InfoTemplateAutoCompleteMenu(TextEditor editor) : base(editor) {
        Shown += (_, _) => {
            // Reset state
            Entries.Clear();
            needEntryRefresh = true;
        };
    }

    private const string StorageKeySeparator = "__";
    private const string BaseStorageKey = $"AutoCompleteInfoTemplate{StorageKeySeparator}";

    public override void Refresh(bool open = true) {
        string fullLine = Document.Lines[Document.Caret.Row];
        Document.Caret.Col = Math.Clamp(Document.Caret.Col, 0, fullLine.Length);

        // Only show when we are inside a target-query expression
        if ((Document.Caret.Col == 0 || fullLine.LastIndexOf('{', startIndex: Document.Caret.Col - 1) is var queryStartIdx && queryStartIdx == -1) ||
            (fullLine.IndexOf('}', startIndex: queryStartIdx + 1, count: Document.Caret.Col - queryStartIdx - 1) is var queryEndIdx && queryEndIdx != -1 && queryEndIdx < Document.Caret.Col)
        ) {
            editor.ClosePopupMenu();
            return;
        }

        if (open) {
            editor.OpenPopupMenu(this);
        }

        var loadingEntry = new Entry {
            DisplayText = string.Empty,
            SearchText = string.Empty,
            ExtraText = string.Empty,
            OnUse = null!,
            Disabled = true,
        };

        if (needEntryRefresh) {
            Entries.Clear();
            Entries.Add(loadingEntry);
            editor.RecalcPopupMenu();
        }

        tokenSource?.Cancel();
        tokenSource?.Dispose();
        tokenSource = new CancellationTokenSource();

        var token = tokenSource.Token;
        Task.Run(async () => {
            try {
                int loadingDots = 0;

                while (!token.IsCancellationRequested && await Application.Instance.InvokeAsync(() => Visible).ConfigureAwait(false)) {
                    if (!CommunicationWrapper.Connected) {
                        loadingEntry.DisplayText = "Connection with Celeste required for target-query auto-complete!";
                        await Application.Instance.InvokeAsync(() => {
                            Entries.Clear();
                            Entries.Add(loadingEntry);
                            editor.RecalcPopupMenu();
                        }).ConfigureAwait(false);

                        break;
                    }

                    loadingDots = (loadingDots + 1).Mod(4);
                    loadingEntry.DisplayText = $"Loading{new string('.', loadingDots)}{new string(' ', 3 - loadingDots)}";

                    (var commandEntries, bool done) = await CommunicationWrapper.RequestAutoCompleteEntries(
                        CommandInfo.GetCommand, [fullLine[(queryStartIdx + 1)..Document.Caret.Col]], Document.FilePath, Document.Caret.Row, refresh: needEntryRefresh)
                            .ConfigureAwait(false);
                    needEntryRefresh = false; // Clear flag to avoid re-requesting every loop iterator

                    var menuEntries = commandEntries.Select(entry => new Entry {
                        SearchText = entry.Prefix + entry.Name,
                        DisplayText = entry.Name,
                        ExtraText = entry.Extra,
                        Suggestion = entry.Suggestion,
                        StorageKey = entry.StorageKey == null ? null : $"{BaseStorageKey}{StorageKeySeparator}{entry.StorageKey}",
                        StorageName = entry.StorageName,
                        OnUse = () => {
                            using var __ = Document.Update();

                            string insert = entry.FullName;
                            if (entry.IsDone) {
                                if (Document.Caret.Col < fullLine.Length && fullLine[Document.Caret.Col] == '}') {
                                    Document.ReplaceRangeInLine(Document.Caret.Row, queryStartIdx + 1, Document.Caret.Col, insert);
                                    Document.Caret.Col = editor.DesiredVisualCol = queryStartIdx + 1 + insert.Length + 1;
                                } else {
                                    Document.ReplaceRangeInLine(Document.Caret.Row, queryStartIdx + 1, Document.Caret.Col, insert + "}");
                                    Document.Caret.Col = editor.DesiredVisualCol = queryStartIdx + 1 + insert.Length + 1;
                                }
                                Document.Selection.Clear();

                                editor.ClosePopupMenu();
                            } else {
                                Document.ReplaceRangeInLine(Document.Caret.Row, queryStartIdx + 1, Document.Caret.Col, insert);
                                Document.Caret.Col = editor.DesiredVisualCol = queryStartIdx + 1 + insert.Length;
                                Document.Selection.Clear();

                                Refresh();
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

        Filter = fullLine[(queryStartIdx + 1)..Document.Caret.Col];
    }
}
