using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class SnippetDialog : Dialog<bool> {
    private readonly List<Snippet> snippets;
    
    private SnippetDialog() {
        // Create a copy, to not modify the list in Settings before confirming
        snippets = Settings.Instance.Snippets.Select(snippet => snippet.Clone()).ToList();

        var list = new StackLayout {
            Padding = 10,
            Spacing = 10,
        };
        GenerateListEntries(list.Items);
        
        var addButton = new Button { Text = "Add new Snippet" };
        addButton.Click += (_, _) => {
            snippets.Add(new());
            GenerateListEntries(list.Items);
        };
        
        Title = "Edit Snippets";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items = {
                new StackLayout {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Items = { addButton, new  LinkButton { Text = "Open documentation (TODO)" } }
                },
                new Scrollable {
                    MinimumSize = new Size(300, 300),
                    Content = list,
                }
            }
        };
        
        DefaultButton = new Button((_, _) => Close(true)) { Text = "&OK" };
        AbortButton = new Button((_, _) => Close(false)) { Text = "&Cancel" };
        
        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);
        
        // Prevent handling key presses which were meant for a hotkey button
        KeyDown += (_, e) => e.Handled = true;
    }
    
    private void GenerateListEntries(ICollection<StackLayoutItem> items) {
        items.Clear();
        
        // Hack to unfocus the selected button once a hotkey has been pressed
        var unfocuser = new Button { Visible = false };
        items.Add(unfocuser);
        
        for (int i = 0; i < snippets.Count; i++) {
            var snippet = snippets[i];

            var enabledCheckBox = new CheckBox {Checked = snippet.Enabled};
            enabledCheckBox.CheckedChanged += (_, _) => snippet.Enabled = enabledCheckBox.Checked.Value;
            
            bool ignoreFocusLoss = false; // Used to prevent the message box from causing an unfocus
            var hotkeyButton = new Button {Text = snippet.Hotkey.ToShortcutString(), ToolTip = "Use the right mouse button to clear a hotkey!", Font = SystemFonts.Bold(), Width = 150};
            hotkeyButton.GotFocus += (_, _) => {
                hotkeyButton.Text = "Press a hotkey...";
                hotkeyButton.Font = SystemFonts.Bold().WithFontStyle(FontStyle.Italic);
            };
            hotkeyButton.LostFocus += (_, _) => {
                if (ignoreFocusLoss) {
                    return;
                }
                
                hotkeyButton.Text = snippet.Hotkey.ToShortcutString();
                hotkeyButton.Font = SystemFonts.Bold();
            };
            hotkeyButton.KeyDown += (_, e) => {
                // Don't allow binding modifiers by themselves
                if (e.Key is Keys.LeftShift or Keys.RightShift
                    or Keys.LeftControl or Keys.RightControl
                    or Keys.LeftAlt or Keys.RightAlt
                    or Keys.LeftApplication or Keys.RightApplication) {
                    return;
                }
                
                // Check for conflicts
                if (snippets.Any(other => other.Hotkey == e.KeyData)) 
                {
                    ignoreFocusLoss = true;
                    var confirm = MessageBox.Show($"Another snippet already uses this hotkey ({e.KeyData.ToShortcutString()}).{Environment.NewLine}Are you sure you to use this hotkey?", MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.Yes);
                    ignoreFocusLoss = false;
                    
                    if (confirm != DialogResult.Yes) {
                        return;
                    }
                }
                
                snippet.Hotkey = e.KeyData;
                unfocuser.Focus();
                
                // Set again in case we already lost focus through the message box
                hotkeyButton.Text = snippet.Hotkey.ToShortcutString();
                hotkeyButton.Font = SystemFonts.Bold();
            };
            hotkeyButton.MouseDown += (_, e) => {
                if (e.Buttons.HasFlag(MouseButtons.Alternate)) {
                    snippet.Hotkey = Keys.None;
                    unfocuser.Focus();
                    
                    // Set again in case we already lost focus through the message box
                    hotkeyButton.Text = snippet.Hotkey.ToShortcutString();
                    hotkeyButton.Font = SystemFonts.Bold();
                    
                    e.Handled = true;
                }
            };
            
            var shortcutTextBox = new TextBox();
            
            var textArea = new TextArea {Text = snippet.Insert, Font = FontManager.EditorFontRegular, Width = 300};
            textArea.TextChanged += (_, _) => snippet.Insert = textArea.Text;
            
            int idx = i;
            var upButton = new Button { Text = "\u2bc5", Enabled = i != 0 };
            upButton.Click += (_, _) => { 
                (snippets[idx], snippets[idx - 1]) = (snippets[idx - 1], snippets[idx]); 
                GenerateListEntries(items);
            };
            
            var downButton = new Button { Text = "\u2bc6", Enabled = i != snippets.Count - 1 };
            downButton.Click += (_, _) => {
                (snippets[idx], snippets[idx + 1]) = (snippets[idx + 1], snippets[idx]);
                GenerateListEntries(items);
            };
            
            var deleteButton = new Button { Text = "Delete" };
            deleteButton.Click += (_, _) => {
                if (MessageBox.Show("Are you sure you want to delete this snippet?", MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.Yes) != DialogResult.Yes) {
                    return;
                }
                
                snippets.RemoveAt(idx);
                GenerateListEntries(items);
            };
            
            var layout = new DynamicLayout {DefaultSpacing = new Size(15, 5), Padding = new Padding(0, 0, 0, 10)};
            {
                layout.BeginHorizontal();
                layout.BeginVertical();
                
                layout.BeginHorizontal();
                layout.AddCentered(new Label {Text = "Enabled"});
                layout.Add(enabledCheckBox);
                layout.EndBeginHorizontal();
                layout.AddCentered(new Label {Text = "Hotkey"});
                layout.Add(hotkeyButton, yscale: true);
                layout.EndBeginHorizontal();
                layout.AddCentered(new Label {Text = "Shortcut"});
                layout.Add(shortcutTextBox);
                layout.EndHorizontal();
                
                layout.EndVertical();
                
                layout.Add(textArea);
                
                layout.BeginVertical();
                layout.Add(upButton);
                layout.Add(downButton);
                layout.Add(deleteButton);
                layout.EndVertical();
                
                layout.EndHorizontal();
            }
            
            items.Add(layout);
        }
    }
    
    public static void Show() {
        var dialog = new SnippetDialog();
        if (!dialog.ShowModal())
            return;
        
        Settings.Instance.Snippets = dialog.snippets;
        Settings.OnChanged();
        Settings.Save();
    }
}