using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class SnippetDialog : Dialog<bool> {
    private class BindingCell(List<Snippet> allSnippets) : CustomCell {
        private static readonly PropertyInfo p_CellEventArgs_IsEditing = typeof(CellEventArgs).GetProperty(nameof(CellEventArgs.IsEditing))!;
        
        protected override Control OnCreateCell(CellEventArgs args) {
            var snippet = (Snippet)args.Item;
            var drawable = new Drawable();
            
            // The clip-rect is different between OnCreateCell and OnPaint.
            // Surely this offset isn't platform dependant...
            drawable.Paint += (_, e) => Draw(e.Graphics, snippet, args.IsEditing, e.ClipRectangle.X + 4.0f, e.ClipRectangle.Y + 4.0f);
            drawable.KeyDown += (_, e) => {
                if (!args.IsEditing)
                    return;
                
                // Don't allow binding modifiers by themselves
                if (e.Key is Keys.LeftShift or Keys.RightShift 
                          or Keys.LeftControl or Keys.RightControl
                          or Keys.LeftAlt or Keys.RightAlt
                          or Keys.LeftApplication or Keys.RightApplication) 
                {
                    return;
                }
                
                // Check for conflicts
                var conflicts = allSnippets.Where(other => other.Shortcut == e.KeyData).ToArray();
                if (conflicts.Length != 0) {
                    var sb = new StringBuilder();
                    sb.AppendLine($"The following other snippets already use this shortcut ({e.KeyData.FormatShortcut(" + ")}):");
                    foreach (var conflict in conflicts) {
                        sb.AppendLine($" - \"{conflict.Text}\"");
                    }
                    sb.AppendLine(string.Empty);
                    sb.AppendLine("Are you sure you want to use this shortcut?");
                    
                    var confirm = MessageBox.Show(sb.ToString(), MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.No);
                    if (confirm == DialogResult.No)
                        return;
                }
                
                // We cannot just call grid.CommitEdit() to end the editing...
                // So we kinda need to do this weird workaround
                p_CellEventArgs_IsEditing.SetValue(args, false);
                
                snippet.Shortcut = e.KeyData;
                drawable.Invalidate();
            };
            drawable.CanFocus = true;
            drawable.Focus();
            
            return drawable;
        }
        
        protected override void OnPaint(CellPaintEventArgs args)
        {
            var snippet = (Snippet)args.Item;
            Draw(args.Graphics, snippet, args.IsEditing, args.ClipRectangle.X, args.ClipRectangle.Y);
        }
        
        private void Draw(Graphics graphics, Snippet snippet, bool editing, float x, float y) {
            var font = editing 
                ? SystemFonts.Bold().WithFontStyle(FontStyle.Bold | FontStyle.Italic)
                : SystemFonts.Bold();
            string text = editing
                ? $"{snippet.Shortcut.FormatShortcut(" + ")}..."
                : snippet.Shortcut.FormatShortcut(" + ");
            
            graphics.DrawText(font, SystemColors.ControlText, x, y, text);
        }
    }
    
    private readonly List<Snippet> snippets;
    
    public SnippetDialog() {
        // Create a copy, to not modify the list in Settings before confirming
        snippets = Settings.Snippets.Select(snippet => snippet.Clone()).ToList();
        
        var grid = new GridView<Snippet> { DataStore = snippets };
        grid.Columns.Add(new GridColumn {
            HeaderText = "Enabled",
            DataCell = new CheckBoxCell(nameof(Snippet.Enabled)),
            Editable = true,
            Width = 65
        });
        grid.Columns.Add(new GridColumn {
            HeaderText = "Shortcut",
            DataCell = new BindingCell(snippets),
            Editable = true,
            Width = 200
        });
        grid.Columns.Add(new GridColumn {
            HeaderText = "Content", 
            DataCell = new TextBoxCell(nameof(Snippet.Text)),
            Editable = true,
            Width = 200
        });
        
        var addButton = new Button { Text = "Add" };
        addButton.Click += (_, _) => {
            snippets.Add(new());
            grid.DataStore = snippets;
        };
        
        var removeButton = new Button { Text = "Remove" };
        removeButton.Click += (_, _) => {
            var confirm = MessageBox.Show("Are you sure you want to delete the selected snippet?", MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.Yes);
            if (confirm == DialogResult.Yes) {
                snippets.Remove(grid.SelectedItem);
                grid.DataStore = snippets;
            }
        };
        
        Title = "Snippets";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            Items = {
                new StackLayout {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Items = { addButton, removeButton }
                },
                grid
            }
        };
        
        DefaultButton = new Button((_, _) => Close(true)) { Text = "&OK" };
        AbortButton = new Button((_, _) => Close(false)) { Text = "&Cancel" };
        
        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);
    }
    
    public static void Show() {
        var dialog = new SnippetDialog();
        if (!dialog.ShowModal())
            return;
        
        Settings.Snippets = dialog.snippets;
        Settings.Instance.OnChanged();
        Settings.Save();
    }
}