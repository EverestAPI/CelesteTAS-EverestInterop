using CelesteStudio.Controls;
using System;
using System.Collections.Generic;
using CelesteStudio.Editing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class FindDialog : Eto.Forms.Dialog {
    private readonly TextViewer viewer;

    private bool needsSearch = true;
    private readonly List<CaretPosition> matches = [];

    private readonly TextBox searchQuery;
    private readonly CheckBox matchCase;

    private FindDialog(TextViewer viewer, string initialText) {
        this.viewer = viewer;

        searchQuery = new TextBox { Text = initialText, PlaceholderText = "Search", Width = 200 };
        matchCase = new CheckBox { Text = "Match Case", Checked = Settings.Instance.FindMatchCase };
        searchQuery.TextChanging += (_, _) => needsSearch = true;
        matchCase.CheckedChanged += (_, _) => needsSearch = true;

        var nextButton = new Button { Text = "Next", Width = 95};
        var prevButton = new Button { Text = "Previous", Width = 95 };
        nextButton.Click += (_, _) => SelectNext();
        prevButton.Click += (_, _) => SelectPrev();

        Title = "Find";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            Items = {
                searchQuery,
                matchCase,
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { nextButton, prevButton }
                }
            },
        };

        KeyDown += HandleKeyDown;
        searchQuery.KeyDown += HandleKeyDown;

        Studio.RegisterDialog(this, viewer.ParentWindow);

        return;

        // Keyboard navigation with Enter=Next, Shift+Enter=Prev
        void HandleKeyDown(object? _, KeyEventArgs e) {
            if (e.Key != Keys.Enter) {
                return;
            }
            e.Handled = true;

            if (e.Shift) {
                SelectPrev();
            } else {
                SelectNext();
            }
        }
    }

    private void SelectNext() {
        if (needsSearch) {
            UpdateMatches();
        }
        if (matches.Count == 0) {
            return;
        }

        // Check against start of selection
        foreach (var match in matches) {
            if (match > viewer.Document.Caret) {
                SelectMatch(match);
                return;
            }
        }
        // Wrap around to start
        SelectMatch(matches[0]);
    }
    private void SelectPrev() {
        if (needsSearch) {
            UpdateMatches();
        }
        if (matches.Count == 0) {
            return;
        }

        // Check against end of selection
        for (int i = matches.Count - 1; i >= 0; i--) {
            var match = matches[i];
            var end = new CaretPosition(match.Row, match.Col + searchQuery.Text.Length);

            if (end < viewer.Document.Caret) {
                SelectMatch(match);
                return;
            }
        }
        // Wrap around to end
        SelectMatch(matches[^1]);
    }

    private void SelectMatch(CaretPosition match) {
        viewer.Document.Caret = match;
        viewer.Document.Selection.Start = match;
        viewer.Document.Selection.End = match with { Col = match.Col + searchQuery.Text.Length };
        viewer.ScrollCaretIntoView(center: true);
        viewer.Invalidate();
    }

    private void UpdateMatches() {
        needsSearch = false;
        var compare = (matchCase.Checked ?? false) ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase;

        matches.Clear();
        string search = searchQuery.Text;
        if (search.Length == 0) {
            return;
        }

        for (int row = 0; row < viewer.Document.Lines.Count; row++) {
            string line = viewer.Document.Lines[row];
            int col = 0;

            while (true) {
                col = line.IndexOf(searchQuery.Text, col, compare);
                if (col < 0) {
                    break;
                }

                matches.Add(new CaretPosition(row, col));
                col += searchQuery.Text.Length;
            }
        }

        if (matches.Count == 0) {
            MessageBox.Show("No matches were found.");
        }
    }

    public static void Show(TextViewer viewer, ref string searchQuery, ref bool matchCase) {
        var dialog = new FindDialog(viewer, searchQuery);
        dialog.ShowModal(viewer);

        searchQuery = dialog.searchQuery.Text;
        matchCase = dialog.matchCase.Checked == true;
    }
}
