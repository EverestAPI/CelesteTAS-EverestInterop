using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CelesteStudio.RichText;

namespace CelesteStudio;

public static class DialogUtils {
    private static string lastFindText = "";

    public static bool ShowInputDialog(string title, ref string input) {
        const int padding = 10;
        const int buttonWidth = 75;
        const int buttonHeight = 30;

        Size size = new(200, buttonHeight * 2 + padding * 3);

        using Form inputBox = new();
        inputBox.TopMost = true;
        inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
        inputBox.ClientSize = size;
        inputBox.Text = title;
        inputBox.StartPosition = FormStartPosition.CenterParent;
        inputBox.MinimizeBox = false;
        inputBox.MaximizeBox = false;

        TextBox textBox = new();
        textBox.Size = new Size(size.Width - padding * 2, buttonHeight);
        textBox.Location = new Point(padding, padding);
        textBox.Font = new Font(FontFamily.GenericSansSerif, 12);
        textBox.ForeColor = Color.FromArgb(50, 50, 50);
        textBox.Text = input;
        inputBox.Controls.Add(textBox);
        inputBox.ClientSize = size = new Size(size.Width, textBox.Height + buttonHeight + padding * 3);

        Button okButton = new();
        okButton.DialogResult = DialogResult.OK;
        okButton.Name = "okButton";
        okButton.Size = new Size(buttonWidth, buttonHeight);
        okButton.Text = "&OK";
        okButton.Location = new Point(size.Width - buttonWidth * 2 - padding * 2, textBox.Bottom + padding);
        inputBox.Controls.Add(okButton);

        Button cancelButton = new();
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(buttonWidth, buttonHeight);
        cancelButton.Text = "&Cancel";
        cancelButton.Location = new Point(size.Width - buttonWidth - padding, textBox.Bottom + padding);
        inputBox.Controls.Add(cancelButton);

        inputBox.AcceptButton = okButton;
        inputBox.CancelButton = cancelButton;

        DialogResult result = inputBox.ShowDialog();
        input = textBox.Text;

        return result == DialogResult.OK;
    }

    public static void ShowFindDialog(RichText.RichText richText) {
        const int padding = 10;
        const int buttonWidth = 75;
        const int buttonHeight = 30;

        Size size = new(300, buttonHeight * 2 + padding * 3);
        bool pressEnter = false;

        using Form inputBox = new();
        inputBox.TopMost = true;
        inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
        inputBox.ClientSize = size;
        inputBox.Text = "Find";
        inputBox.StartPosition = FormStartPosition.CenterParent;
        inputBox.MinimizeBox = false;
        inputBox.MaximizeBox = false;
        inputBox.KeyPreview = true;
        inputBox.KeyDown += (sender, args) => {
            if (args.KeyCode == Keys.Escape) {
                inputBox.Close();
            } else {
                pressEnter = args.KeyCode == Keys.Enter;
            }
        };

        TextBox textBox = new();
        textBox.Size = new Size(size.Width - 3 * padding - buttonWidth, buttonHeight);
        textBox.Location = new Point(padding, padding);
        textBox.Font = new Font(FontFamily.GenericSansSerif, 12);
        textBox.ForeColor = Color.FromArgb(50, 50, 50);
        textBox.Text = richText.SelectedText.Length == 0 ? lastFindText : richText.SelectedText;
        textBox.SelectAll();
        textBox.KeyDown += (sender, args) => pressEnter = args.KeyCode == Keys.Enter;
        textBox.KeyPress += (sender, args) => {
            if (pressEnter) {
                args.Handled = true;
                QuickFind(richText, textBox.Text, true);
            }
        };
        textBox.KeyUp += (sender, args) => lastFindText = textBox.Text;

        inputBox.KeyPress += (sender, args) => {
            if (pressEnter) {
                args.Handled = true;
                QuickFind(richText, textBox.Text, true);
            }
        };
        inputBox.Controls.Add(textBox);

        Button previousButton = new();
        previousButton.Name = "previouButton";
        previousButton.Size = new Size(buttonWidth, buttonHeight);
        previousButton.Text = "&Previous";
        previousButton.Location = new Point(textBox.Right + padding, textBox.Top);
        previousButton.Click += (sender, args) => QuickFind(richText, textBox.Text, false);
        inputBox.Controls.Add(previousButton);

        Button nextButton = new();
        nextButton.Name = "nextButton";
        nextButton.Size = new Size(buttonWidth, buttonHeight);
        nextButton.Text = "&Next";
        nextButton.Location = new Point(previousButton.Left, previousButton.Bottom + padding);
        nextButton.Click += (sender, args) => QuickFind(richText, textBox.Text, true);
        inputBox.Controls.Add(nextButton);

        CheckBox caseCheckbox = new();
        caseCheckbox.Size = new Size(textBox.Width, buttonHeight);
        caseCheckbox.Text = "&Match case";
        caseCheckbox.Location = new Point(textBox.Left, nextButton.Top);
        caseCheckbox.Checked = Settings.Instance.FindMatchCase;
        caseCheckbox.CheckedChanged += (sender, args) => Settings.Instance.FindMatchCase = caseCheckbox.Checked;
        inputBox.Controls.Add(caseCheckbox);

        inputBox.ShowDialog();
    }

    private static void QuickFind(RichText.RichText richText, string text, bool next, bool fromStart = false) {
        if (string.IsNullOrEmpty(text)) {
            return;
        }

        Range selection = richText.Selection;
        selection.Normalize();

        int startLine;
        int? startIndex;
        if (next) {
            startLine = selection.Start.iLine;
            startIndex = selection.Start.iChar + selection.Text.Length;
        } else {
            startLine = selection.End.iLine;
            startIndex = selection.End.iChar - selection.Text.Length;
        }

        if (fromStart) {
            startLine = next ? 0 : richText.LinesCount - 1;
            startIndex = next ? 0 : richText.Lines.LastOrDefault()?.Length ?? 0;
        }

        int? resultLine = null;
        int? resultIndex = null;
        StringComparison comparison =
            Settings.Instance.FindMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase;
        if (next) {
            for (int i = startLine; i < richText.LinesCount; i++) {
                startIndex ??= 0;
                string lineText = richText[i].Text;
                int textLength = lineText.Length;
                if (textLength >= startIndex.Value &&
                    lineText.Substring(startIndex.Value, lineText.Length - startIndex.Value)
                        .IndexOf(text, comparison) is var findIndex and >= 0) {
                    resultLine = i;
                    resultIndex = findIndex + startIndex;
                    break;
                } else {
                    startIndex = null;
                }
            }
        } else {
            for (int i = startLine; i >= 0; i--) {
                startIndex ??= richText[i].Text.Length;
                if (startIndex.Value >= 0 &&
                    richText[i].Text.Substring(0, startIndex.Value).LastIndexOf(text, StringComparison.InvariantCultureIgnoreCase) is var
                        findIndex and >= 0) {
                    resultLine = i;
                    resultIndex = findIndex;
                    break;
                } else {
                    startIndex = null;
                }
            }
        }

        if (resultLine is { } line && resultIndex is { } index) {
            richText.Selection = new Range(richText, index, line, index + text.Length, line);
            richText.DoSelectionVisible();
        } else {
            if (fromStart) {
                MessageBox.Show($"Can't find the text \"{text}\"", "Celeste Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } else {
                QuickFind(richText, text, next, true);
            }
        }
    }

    public static void ShowGoToDialog(RichText.RichText richText) {
        const int padding = 10;
        const int buttonWidth = 75;
        const int buttonHeight = 30;

        Size size = new(245, buttonHeight * 2 + (int) (padding * 2.5));
        bool pressEnter = false;

        using Form inputBox = new();
        inputBox.TopMost = true;

        void GoToLine(int line) {
            richText.GoToLine(line);
            inputBox.Close();
        }

        inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
        inputBox.ClientSize = size;
        inputBox.Text = "Go to Line/Label";
        inputBox.StartPosition = FormStartPosition.CenterParent;
        inputBox.MinimizeBox = false;
        inputBox.MaximizeBox = false;
        inputBox.KeyPreview = true;
        inputBox.KeyDown += (sender, args) => {
            if (args.KeyCode == Keys.Escape) {
                inputBox.Close();
            } else {
                pressEnter = args.KeyCode == Keys.Enter;
            }
        };

        Label lineNumberLabel = new();
        lineNumberLabel.AutoSize = true;
        lineNumberLabel.Text = "Line  ";
        inputBox.Controls.Add(lineNumberLabel);

        richText.Selection.Normalize();
        int currentLine = richText.Selection.Start.iLine;

        TextBox lineNumberTextBox = new();
        lineNumberTextBox.AutoSize = true;
        lineNumberTextBox.Location = new Point(lineNumberLabel.Right + padding, padding);
        lineNumberTextBox.Font = new Font(FontFamily.GenericSansSerif, 11);
        lineNumberTextBox.ForeColor = Color.FromArgb(50, 50, 50);
        lineNumberTextBox.Text = (currentLine + 1).ToString();
        lineNumberTextBox.SelectAll();
        lineNumberTextBox.KeyDown += (sender, args) => pressEnter = args.KeyCode == Keys.Enter;
        lineNumberTextBox.KeyPress += (sender, args) => {
            if (pressEnter) {
                args.Handled = true;
                if (int.TryParse(lineNumberTextBox.Text, out int line)) {
                    GoToLine(line - 1);
                }
            }
        };
        inputBox.KeyPress += (sender, args) => {
            if (pressEnter) {
                args.Handled = true;
                if (int.TryParse(lineNumberTextBox.Text, out int line)) {
                    GoToLine(line - 1);
                }
            }
        };
        inputBox.Controls.Add(lineNumberTextBox);
        lineNumberLabel.Location = new Point(padding, padding + (lineNumberTextBox.Height - lineNumberLabel.Height) / 2);

        Button goToLineButton = new();
        goToLineButton.Name = "goToLineButton";
        goToLineButton.Text = "&OK";
        goToLineButton.Size = new Size(buttonWidth, buttonHeight);
        goToLineButton.Location = new Point(lineNumberTextBox.Right + padding, padding - (buttonHeight - lineNumberTextBox.Height) / 2);
        goToLineButton.Click += (sender, args) => {
            if (int.TryParse(lineNumberTextBox.Text, out int line)) {
                GoToLine(line - 1);
            }
        };
        inputBox.Controls.Add(goToLineButton);

        Label commentLabel = new();
        commentLabel.AutoSize = true;
        commentLabel.Text = "Label ";
        inputBox.Controls.Add(commentLabel);

        ComboBox roomComboBox = new();

        Regex labelRegex = new(@"^\s*#[^\s#]");
        Regex commentCommandRegex = new(@"^\s*#(play|console|set)(\s|,)", RegexOptions.IgnoreCase);
        Regex roomRegex = new(@"^\s*#(lvl_)?");
        for (int i = 0; i < richText.Lines.Count; i++) {
            string lineText = richText.Lines[i];
            if (labelRegex.IsMatch(lineText) && !commentCommandRegex.IsMatch(lineText)) {
                roomComboBox.Items.Add(new RoomNameItem(i, roomRegex.Replace(lineText, string.Empty)));

                if (currentLine >= i) {
                    roomComboBox.SelectedIndex = roomComboBox.Items.Count - 1;
                }
            }
        }

        roomComboBox.Size = new Size(size.Width - commentLabel.Width - padding * 2, 1);
        roomComboBox.Location = new Point(commentLabel.Width + padding, lineNumberTextBox.Bottom + padding);
        roomComboBox.Font = new Font(FontFamily.GenericSansSerif, 11);
        roomComboBox.ForeColor = Color.FromArgb(50, 50, 50);
        roomComboBox.Click += (sender, args) => roomComboBox.DroppedDown = true;
        roomComboBox.Enter += (sender, args) => roomComboBox.DroppedDown = true;
        roomComboBox.KeyDown += (sender, args) => {
            if (args.KeyCode == Keys.Enter && roomComboBox.SelectedItem is RoomNameItem roomNameItem) {
                GoToLine(roomNameItem.LineNumber);
            }
        };
        roomComboBox.SelectedIndexChanged += (sender, args) => {
            if (!roomComboBox.DroppedDown && roomComboBox.SelectedItem is RoomNameItem roomNameItem) {
                GoToLine(roomNameItem.LineNumber);
            }
        };

        inputBox.KeyPress += (sender, args) => {
            if (pressEnter) {
                args.Handled = true;
                if (int.TryParse(lineNumberTextBox.Text, out int line)) {
                    GoToLine(line - 1);
                }
            }
        };
        inputBox.Controls.Add(roomComboBox);
        commentLabel.Location = new Point(padding, roomComboBox.Bottom - commentLabel.Height);

        inputBox.ShowDialog();
    }

    private record RoomNameItem {
        public readonly int LineNumber;
        public readonly string RoomName;

        public RoomNameItem(int lineNumber, string roomName) {
            LineNumber = lineNumber;
            RoomName = roomName;
        }

        public override string ToString() {
            return RoomName;
        }
    }
}