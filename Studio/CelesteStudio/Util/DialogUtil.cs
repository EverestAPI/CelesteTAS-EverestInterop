using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CelesteStudio.Editing;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class DialogUtil
{
    public static T ShowNumberInputDialog<T>(string title, T input, T minValue, T maxValue, T step) where T : INumber<T> {
        var stepper = new NumericStepper {
            Value = double.CreateChecked(input),
            MinValue = double.CreateChecked(minValue),
            MaxValue = double.CreateChecked(maxValue),
            Increment = double.CreateChecked(step),
            Width = 200,
        };

        if (input is int)
            stepper.DecimalPlaces = stepper.MaximumDecimalPlaces = 0;
        else
            stepper.DecimalPlaces = 2;

        var dialog = new Dialog<T> {
            Title = title,
            Content = new StackLayout {
                Padding = 10,
                Items = { stepper },
            }
        };

        dialog.DefaultButton = new Button((_, _) => dialog.Close(T.CreateChecked(stepper.Value))) { Text = "&OK" };
        dialog.AbortButton = new Button((_, _) => dialog.Close()) { Text = "&Cancel" };
        
        dialog.PositiveButtons.Add(dialog.DefaultButton);
        dialog.NegativeButtons.Add(dialog.AbortButton);
        dialog.Result = input;
            
        return dialog.ShowModal();
    }
    
    private class FontPreview : Drawable {
        private Font font = null!;
        private SyntaxHighlighter? highlighter;
        
        public void SetFont(string fontFamily, float size) {
            font = FontManager.CreateFont(fontFamily, size);
            highlighter = new SyntaxHighlighter(font,
                FontManager.CreateFont(fontFamily, size, FontStyle.Bold),
                FontManager.CreateFont(fontFamily, size, FontStyle.Italic),
                FontManager.CreateFont(fontFamily, size, FontStyle.Bold | FontStyle.Italic));
            
            Invalidate();
        }
        
        public FontPreview() {
            BackgroundColor = Settings.Instance.Theme.Background;
            Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.Background;
        }
        
        protected override void OnPaint(PaintEventArgs e) {
            if (highlighter == null)
                return;
            
            string[] previewText = [
                "#Start",
                "   7,L,Z",
                "***S",
                "  38,R,J,AD",
                "  17,F,192.59",
                "Set, Player.Speed.X, 200",
                " 145,R,D",
            ];
            
            e.Graphics.AntiAlias = true;
            
            float yPos = 0.0f;
            float maxWidth = 0.0f;
            foreach (var line in previewText) {
                highlighter.DrawLine(e.Graphics, 0.0f, yPos, line);
                maxWidth = Math.Max(maxWidth, font.MeasureWidth(line));
                yPos += font.LineHeight();
            }
            
            Size = new((int)maxWidth, (int)yPos);
            
            base.OnPaint(e);
        }
    }
    
    private static string[]? cachedFontFamilys;
    
    public static void ShowFontDialog() {
        var preview = new FontPreview();
        preview.SetFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize);
        string fontFamily = Settings.Instance.FontFamily;
        
        var editorFontSize = new NumericStepper {
            Value = Settings.Instance.EditorFontSize,
            MinValue = 1.0f,
            Increment = 1.0f,
            DecimalPlaces = 1,
            Width = 100,
        };
        var statusFontSize = new NumericStepper {
            Value = Settings.Instance.StatusFontSize,
            MinValue = 1.0f,
            Increment = 1.0f,
            DecimalPlaces = 1,
            Width = 100,
        };
        
        var dialog = new Dialog<bool> {
            Title = "Font",
            Content = CreateDialogContent(),
        };
        
        dialog.DefaultButton = new Button((_, _) => dialog.Close(true)) { Text = "&OK" };
        dialog.AbortButton = new Button((_, _) => dialog.Close(false)) { Text = "&Cancel" };
        
        dialog.PositiveButtons.Add(dialog.DefaultButton);
        dialog.NegativeButtons.Add(dialog.AbortButton);
        
        // Fetch font list asynchronously
        if (cachedFontFamilys == null) {
            Task.Run(() => {
                var fonts = new List<string>();
                foreach (var family in Fonts.AvailableFontFamilies) {
                    // Check if the font is monospaced
                    var font = new Font(family, 12.0f);
                    if (Math.Abs(font.MeasureString("I").Width - font.MeasureString("X").Width) > 0.01f)
                        continue;
                    
                    fonts.Add(family.Name);
                }
                cachedFontFamilys = fonts.ToArray();
                dialog.Content = CreateDialogContent();
            });
        }
        
        if (!dialog.ShowModal())
            return;
        
        Settings.Instance.FontFamily = fontFamily;
        Settings.Instance.EditorFontSize = (float)editorFontSize.Value;
        Settings.Instance.StatusFontSize = (float)statusFontSize.Value;
        Settings.Instance.OnFontChanged();
        Settings.Save();
        
        return;
        
        Control CreateDialogContent() {
            var settingsPanel = new StackLayout {
                Padding = 10,
                Spacing = 10,
                Items = {
                    new StackLayout {
                        Spacing = 10,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Orientation = Orientation.Horizontal,
                        Items = { new Label { Text = "Editor Font Size" }, editorFontSize },
                    },
                    new StackLayout {
                        Spacing = 10,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Orientation = Orientation.Horizontal,
                        Items = { new Label { Text = "Status Font Size" }, statusFontSize },
                    },
                    new Label { Text = "  Preview:" }.WithFontStyle(FontStyle.Bold),
                    new Scrollable {
                        Content = preview,
                        Width = 250,
                        Height = 200,
                    }
                }
            };
            
            if (cachedFontFamilys != null) {
                var list = new ListBox { Width = 250, Height = 330 };
                list.SelectedValue = Settings.Instance.FontFamily;
                
                // Add built-in font
                list.Items.Add(new ListItem {
                    Text = FontManager.FontFamilyBuiltinDisplayName,
                    Key = FontManager.FontFamilyBuiltin,
                });
                foreach (var family in cachedFontFamilys) {
                    list.Items.Add(new ListItem {
                        Text = family,
                        Key = family,
                    });
                }
                
                // Select current font
                foreach (var item in list.Items) {
                    if (item.Key == Settings.Instance.FontFamily) {
                        list.SelectedValue = item;
                        break;
                    }
                        
                }
                
                editorFontSize.ValueChanged += (_, _) => UpdateFont();
                list.SelectedValueChanged += (_, _) => UpdateFont();
                
                return new StackLayout {
                    Padding = 10,
                    Orientation = Orientation.Horizontal,
                    Items = { list, settingsPanel }
                };
                
                void UpdateFont() {
                    if (list.SelectedValue is not ListItem item)
                        return;
                    
                    fontFamily = item.Key;
                    preview.SetFont(item.Key, (float)editorFontSize.Value);
                }
            } else {
                var loadingPanel = new StackLayout {
                    Spacing = 10,
                    Width = 250, 
                    Height = 330,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = {
                        new Spinner { Enabled = true },
                        new Label { Text = "Loading font list..." }.WithFontStyle(FontStyle.Italic),
                    },
                };
                
                return new StackLayout {
                    Padding = 10,
                    Orientation = Orientation.Horizontal,
                    Items = { loadingPanel, settingsPanel }
                };
            }
        }
    }
    
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
    
    public static void ShowSnippetDialog() {
        // Create a copy, to not modify the list in Settings before confirming
        var snippets = Settings.Snippets.Select(snippet => snippet.Clone()).ToList();
        
        var grid = new GridView<Snippet> { DataStore = snippets };
        grid.Columns.Add(new GridColumn {
            HeaderText = "Enabled",
            DataCell = new CheckBoxCell(nameof(Snippet.Enabled)),
            Editable = true,
            Width = 50
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
        
        var dialog = new Dialog<bool> {
            Title = "Snippets",
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
            }
        };
        
        dialog.DefaultButton = new Button((_, _) => dialog.Close(true)) { Text = "&OK" };
        dialog.AbortButton = new Button((_, _) => dialog.Close(false)) { Text = "&Cancel" };
        
        dialog.PositiveButtons.Add(dialog.DefaultButton);
        dialog.NegativeButtons.Add(dialog.AbortButton);
        
        if (!dialog.ShowModal())
            return;
        
        foreach (Snippet snippet in snippets)
        {
            Console.WriteLine($"{snippet.Shortcut} -> {snippet.Text}");
        }
        
        Settings.Snippets = snippets;
        Settings.Instance.OnChanged();
        Settings.Save();
    }
    
    public static void ShowRecordDialog() {
        var textBox = new TextBox { Text = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}", Width = 200 };
        
        var dialog = new Dialog<bool> {
            Title = "Record TAS",
            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                VerticalContentAlignment = VerticalAlignment.Center, 
                Orientation = Orientation.Horizontal,
                Items = { new Label { Text = "File Name" }, textBox },
            }
        };
        
        dialog.DefaultButton = new Button((_, _) => dialog.Close(true)) { Text = "&Record" };
        dialog.AbortButton = new Button((_, _) => dialog.Close(false)) { Text = "&Cancel" };
        
        dialog.PositiveButtons.Add(dialog.DefaultButton);
        dialog.NegativeButtons.Add(dialog.AbortButton);
        
        if (!dialog.ShowModal())
            return;
        
        if (string.IsNullOrWhiteSpace(textBox.Text)) {
            MessageBox.Show("An empty file name is not valid!", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }

        Studio.CommunicationWrapper.Server.RecordTAS(textBox.Text);
    }
}