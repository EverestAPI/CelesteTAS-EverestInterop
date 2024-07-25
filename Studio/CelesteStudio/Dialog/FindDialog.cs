using System;
using System.Collections.Generic;
using System.Linq;
using CelesteStudio.Editing;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class FindDialog : Eto.Forms.Dialog {
    private readonly Editor editor;
    
    private bool needsSearch = true;
    private readonly List<CaretPosition> matches = [];
    
    private readonly TextBox textBox;
    private readonly CheckBox matchCase;
    
    private FindDialog(Editor editor) {
        this.editor = editor;
        
        textBox = new TextBox { PlaceholderText = "Search", Width = 200 };
        matchCase = new CheckBox { Text = "Match Case", Checked = Settings.Instance.FindMatchCase };
        textBox.TextChanging += (_, _) => needsSearch = true;
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
                textBox,
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
        Icon = Assets.AppIcon;

        DefaultButton = nextButton;

        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }
    
    private void SelectNext() {
        if (needsSearch)
            UpdateMatches();
        if (matches.Count == 0)
            return;
        
        // Check against start of selection
        foreach (var match in matches) {
            if (match > editor.Document.Caret) {
                editor.Document.Caret = match;
                editor.ScrollCaretIntoView(center: true);
                return;
            }
        }
        // Wrap around to start
        editor.Document.Caret = matches[0];
        editor.ScrollCaretIntoView(center: true);
    }
    private void SelectPrev() {
        if (needsSearch)
            UpdateMatches();
        if (matches.Count == 0)
            return;
        
        // Check against end of selection
        for (int i = matches.Count - 1; i >= 0; i--) {
            var match = matches[i];
            var end = new CaretPosition(match.Row, match.Col + textBox.Text.Length);
            
            if (end < editor.Document.Caret) {
                editor.Document.Caret = match;
                editor.ScrollCaretIntoView(center: true);
                return;
            }
        }
        // Wrap around to end
        editor.Document.Caret = matches[^1];
        editor.ScrollCaretIntoView(center: true);
    } 
    
    private void UpdateMatches() {
        needsSearch = false;
        var compare = (matchCase.Checked ?? false) ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase;

        matches.Clear();
        var search = textBox.Text; 
        if (search.Length == 0)
            return;
        
        for (int row = 0; row < editor.Document.Lines.Count; row++) {
            var line = editor.Document.Lines[row];
            int col = 0;
            
            while (true) {
                int idx = line.IndexOf(textBox.Text, col, compare);
                if (idx < 0)
                    break;
                
                matches.Add(new CaretPosition(row, col));
                col = idx + textBox.Text.Length;
            }
        }
        
        if (matches.Count == 0) {
            MessageBox.Show("No matches were found.");
        }
    }
    
    public static void Show(Editor editor) => new FindDialog(editor).ShowModal();
}