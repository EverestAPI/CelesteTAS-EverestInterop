using Eto.Drawing;
using Eto.Forms;
using System.Collections.Generic;
using System.Reflection;
using CelesteStudio.Editing;
using CelesteStudio.Util;

namespace CelesteStudio;

public sealed class ThemeEditor : Form {
    private readonly DropDown selector;
    private readonly DynamicLayout fieldsLayout;
    private readonly CheckBox darkModeCheckBox;
    private readonly Button renameButton;

    // Due to a bug(?) in the GTK impl, ColorPickers have to be manually Disposed
    // see https://github.com/picoe/Eto/issues/2664 for more details
    private readonly List<ColorPicker> pickers = [];

    public ThemeEditor() {
        Title = "Theme Editor";
        Icon = Assets.AppIcon;

        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 10), Padding = new Padding(10), Width = 800, Height = 600 };
        layout.BeginHorizontal();

        // Left side
        layout.BeginVertical();

        selector = new DropDown();
        foreach (var name in Theme.BuiltinThemes.Keys) {
            selector.Items.Add(new ListItem { Text = name + " (builtin)", Key = name });
        }
        foreach (var name in Settings.Instance.CustomThemes.Keys) {
            selector.Items.Add(new ListItem { Text = name, Key = name });
        }
        selector.SelectedKey = Settings.Instance.ThemeName;
        selector.SelectedKeyChanged += (_, _) => {
            if (!string.IsNullOrEmpty(selector.SelectedKey) && selector.SelectedKey != Settings.Instance.ThemeName) {
                Settings.Instance.ThemeName = selector.SelectedKey;
                InitializeThemeParameters();
            }
        };
        Settings.ThemeChanged += () => {
            selector.SelectedKey = Settings.Instance.ThemeName;
            InitializeThemeParameters();
        };
        layout.AddAutoSized(selector);

        layout.Add(renameButton = new Button((_, _) => Rename()) { Text = "Rename" });
        layout.Add(new Button((_, _) => Duplicate()) { Text = "Duplicate" });
        layout.Add(new Button((_, _) => Delete()) { Text = "Delete" });

        layout.BeginHorizontal();
        layout.Add(new Label { Text = "Dark Mode" });
        darkModeCheckBox = new CheckBox();
        darkModeCheckBox.CheckedChanged += (_, _) => {
            UpdateField(typeof(Theme).GetField(nameof(Theme.DarkMode))!, darkModeCheckBox.Checked!);
        };
        layout.Add(darkModeCheckBox);
        layout.EndHorizontal();

        layout.AddSpace();
        layout.EndVertical();

        // Right side
        layout.BeginVertical();
        layout.BeginScrollable();

        fieldsLayout = new DynamicLayout { DefaultSpacing = new Size(10, 10), Padding = new Padding(15) };
        layout.Add(fieldsLayout);

        layout.EndScrollable();
        layout.EndVertical();

        layout.EndHorizontal();

        InitializeThemeParameters();

        Content = layout;
        Resizable = true;
        
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }
    
    private bool IsBuiltin() {
        return Theme.BuiltinThemes.ContainsKey(Settings.Instance.ThemeName);
    }
    
    private void Rename() {
        var newName = AskString("New name for the theme:", Settings.Instance.ThemeName)?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == Settings.Instance.ThemeName) {
            return;
        }
        
        if (Theme.BuiltinThemes.ContainsKey(newName) || Settings.Instance.CustomThemes.ContainsKey(newName)) {
            MessageBox.Show($"'{newName}' already exists", MessageBoxType.Error);
            return;
        }
        
        var prevName = Settings.Instance.ThemeName;
        Settings.Instance.CustomThemes[newName] = Settings.Instance.Theme;
        Settings.Instance.CustomThemes.Remove(prevName);
        Settings.Instance.ThemeName = newName;
        
        // Regenerate theme selector
        selector.Items.Clear();
        foreach (var name in Theme.BuiltinThemes.Keys) {
            selector.Items.Add(new ListItem { Text = name + " (builtin)", Key = name });
        }
        foreach (var name in Settings.Instance.CustomThemes.Keys) {
            selector.Items.Add(new ListItem { Text = name, Key = name });
        }
        selector.SelectedKey = Settings.Instance.ThemeName;
        
        Settings.OnChanged();
        Settings.Save();
    }
    
    private void Duplicate() {
        var newName = AskString("Name for the new theme:", Settings.Instance.ThemeName)?.Trim();
        if (string.IsNullOrEmpty(newName)) {
            return;
        }
        
        if (Theme.BuiltinThemes.ContainsKey(newName) || Settings.Instance.CustomThemes.ContainsKey(newName)) {
            MessageBox.Show($"'{newName}' already exists", MessageBoxType.Error);
            return;
        }
        
        selector.Items.Add(new ListItem { Text = newName, Key = newName });
        Settings.Instance.CustomThemes[newName] = Settings.Instance.Theme;
        Settings.Instance.ThemeName = newName;
        
        Settings.OnChanged();
        Settings.Save();
    }
    
    private void Delete() {
        if (IsBuiltin()) {
            MessageBox.Show("Cannot delete a builtin theme", MessageBoxType.Error);
            return;
        }

        var prevName = Settings.Instance.ThemeName;
        if (MessageBox.Show($"You you sure you want to delete '{prevName}'?",
                    MessageBoxButtons.YesNo,
                    MessageBoxType.Question,
                    MessageBoxDefaultButton.No) != DialogResult.Yes) {
            return;
        }

        // Select next
        bool newIsCurrent = false;
        foreach (var item in selector.Items) {
            if (newIsCurrent) {
                Settings.Instance.ThemeName = item.Key;
                newIsCurrent = false;
                break;
            }
            if (item.Key == prevName) {
                newIsCurrent = true;
            }
        }
        if (newIsCurrent) {
            Settings.Instance.ThemeName = "Light";
        }

        List<IListItem> toDelete = [];
        foreach (var item in selector.Items) {
            if (item.Key == prevName) {
                toDelete.Add(item);
            }
        }
        foreach (var item in toDelete) {
            selector.Items.Remove(item);
        }
        Settings.Instance.CustomThemes.Remove(prevName);

        Settings.OnChanged();
        Settings.Save();
    }

    private void InitializeThemeParameters() {
        // Non-color settings
        darkModeCheckBox.Enabled = !IsBuiltin();
        darkModeCheckBox.Checked = Settings.Instance.Theme.DarkMode;

        renameButton.Enabled = !IsBuiltin();

        // Fields
        fieldsLayout.Clear();
        
        // See the comment on `pickers`
        foreach (var picker in pickers) {
            picker.Dispose();
        }
        pickers.Clear();

        if (IsBuiltin()) {
            return;
        }

        foreach (var field in typeof(Theme).GetFields()) {
            fieldsLayout.BeginHorizontal();
            var value = field.GetValue(Settings.Instance.Theme);

            if (value is int intValue) {
                fieldsLayout.BeginVertical();
                fieldsLayout.AddSpace();
                fieldsLayout.Add(new Label { Text = field.Name });
                fieldsLayout.AddSpace();
                fieldsLayout.EndVertical();
                
                var stepper = new NumericStepper { DecimalPlaces = 0, MaximumDecimalPlaces = 0, Value = intValue };
                stepper.ValueChanged += (_, _) => {
                    UpdateField(field, (int)stepper.Value);
                };
                fieldsLayout.Add(stepper);
            } else if (value is float floatValue) {
                fieldsLayout.BeginVertical();
                fieldsLayout.AddSpace();
                fieldsLayout.Add(new Label { Text = field.Name });
                fieldsLayout.AddSpace();
                fieldsLayout.EndVertical();
                
                var stepper = new NumericStepper { DecimalPlaces = 2, MaximumDecimalPlaces = 2, Value = floatValue };
                stepper.ValueChanged += (_, _) => {
                    UpdateField(field, (float)stepper.Value);
                };
                fieldsLayout.Add(stepper);
            } else if (value is Color color) {
                fieldsLayout.BeginVertical();
                fieldsLayout.AddSpace();
                fieldsLayout.Add(new Label { Text = field.Name });
                fieldsLayout.AddSpace();
                fieldsLayout.EndVertical();

                var picker = new ColorPicker { Value = color, AllowAlpha = true };
                picker.ValueChanged += (_, _) => {
                    UpdateField(field, picker.Value);
                };
                fieldsLayout.Add(picker);
                pickers.Add(picker);
            } else if (value is Style style) {
                fieldsLayout.BeginVertical();
                fieldsLayout.AddSpace();
                fieldsLayout.Add(new Label { Text = field.Name });
                fieldsLayout.AddSpace();
                fieldsLayout.EndVertical();
                
                var styleLayout = new DynamicLayout { DefaultSpacing = new Size(5, 5) };
                // The only reason a Scrollable is used, is because it provides a border
                fieldsLayout.Add(new Scrollable { Content = styleLayout, Padding = 5 }.FixBorder());
                
                styleLayout.BeginVertical();
                styleLayout.BeginHorizontal();

                styleLayout.BeginVertical();
                styleLayout.AddSpace();
                styleLayout.Add(new Label { Text = "Foreground" });
                styleLayout.AddSpace();
                styleLayout.EndVertical();
                
                var fgPicker = new ColorPicker { Value = style.ForegroundColor };
                fgPicker.ValueChanged += (_, _) => {
                    style.ForegroundColor = fgPicker.Value;
                    UpdateField(field, style);
                };
                styleLayout.Add(fgPicker, xscale: true);
                pickers.Add(fgPicker);
                
                styleLayout.EndBeginHorizontal();

                styleLayout.BeginVertical();
                styleLayout.AddSpace();
                styleLayout.Add(new Label { Text = "Background" });
                styleLayout.AddSpace();
                styleLayout.EndVertical();
                
                var bgPicker = new ColorPicker { Value = style.BackgroundColor ?? Colors.Transparent, AllowAlpha = true };
                bgPicker.ValueChanged += (_, _) => {
                    if (bgPicker.Value.A <= 0.001f) {
                        // If the background is null instead of transparent, it helps with performance
                        style.BackgroundColor = null;    
                    } else {
                        style.BackgroundColor = bgPicker.Value;    
                    }
                    UpdateField(field, style);
                };
                styleLayout.Add(bgPicker, xscale: true);
                pickers.Add(bgPicker);
                
                styleLayout.EndBeginHorizontal();
                
                styleLayout.BeginVertical();
                styleLayout.AddSpace();
                styleLayout.AddAutoSized(new Label { Text = "Bold" });
                styleLayout.AddSpace();
                styleLayout.EndVertical();
                
                var bold = new CheckBox { Checked = style.FontStyle.HasFlag(FontStyle.Bold) };
                bold.CheckedChanged += (_, _) => {
                    if (bold.Checked.Value) {
                        style.FontStyle |= FontStyle.Bold;
                    } else {
                        style.FontStyle &= ~FontStyle.Bold;
                    }
                    UpdateField(field, style);
                };
                
                styleLayout.AddAutoSized(bold);
                
                styleLayout.EndBeginHorizontal();
                
                styleLayout.BeginVertical();
                styleLayout.AddSpace();
                styleLayout.AddAutoSized(new Label { Text = "Italic" });
                styleLayout.AddSpace();
                styleLayout.EndVertical();
                
                var italic = new CheckBox { Checked = style.FontStyle.HasFlag(FontStyle.Italic) };
                italic.CheckedChanged += (_, _) => {
                    if (italic.Checked.Value) {
                        style.FontStyle |= FontStyle.Italic;
                    } else {
                        style.FontStyle &= ~FontStyle.Italic;
                    }
                    UpdateField(field, style);
                };
                
                styleLayout.AddAutoSized(italic);
                
                styleLayout.EndHorizontal();
                styleLayout.EndVertical();
                
                /*
                colorsLayout.Add(new Label { Text = field.Name });
                colorsLayout.BeginVertical();

                colorsLayout.BeginHorizontal();
                colorsLayout.Add(new Label { Text = "Foreground" });
                var fgPicker = new ColorPicker { Value = style.ForegroundColor, AllowAlpha = true };
                fgPicker.ValueChanged += (_, _) => {
                    style.ForegroundColor = fgPicker.Value;
                    UpdateField(field, style);
                };
                colorsLayout.Add(fgPicker);
                pickers.Add(fgPicker);

                colorsLayout.EndBeginHorizontal();

                if (style.BackgroundColor is Color bgColor) {
                    colorsLayout.Add(new Label { Text = "Background" });
                    // var enable = new CheckBox { Checked = true };
                    // enable.CheckedChanged += (_, _) => {
                    //     style.BackgroundColor = null;
                    //     UpdateField(field, style);
                    // };
                    // colorsLayout.Add(enable);
                    var bgPicker = new ColorPicker { Value = bgColor, AllowAlpha = true };
                    bgPicker.ValueChanged += (_, _) => {
                        style.BackgroundColor = bgPicker.Value;
                        UpdateField(field, style);
                    };
                    colorsLayout.Add(bgPicker);
                    pickers.Add(bgPicker);
                } else {
                    // colorsLayout.Add(new Label { Text = "Background" });
                    // var enable = new CheckBox { Checked = false };
                    // enable.CheckedChanged += (_, _) => {
                    //     style.BackgroundColor = Color.FromRgb(0);
                    //     UpdateField(field, style);
                    // };
                    // colorsLayout.Add(enable);
                }

                colorsLayout.EndBeginHorizontal();

                colorsLayout.Add(new Label { Text = "Italic" });
                var italic = new CheckBox { Checked = (style.FontStyle & FontStyle.Italic) != 0 };
                italic.CheckedChanged += (_, _) => {
                    if (italic.Checked is bool it) {
                        if (it) {
                            style.FontStyle |= FontStyle.Italic;
                        } else {
                            style.FontStyle &= ~FontStyle.Italic;
                        }
                    }
                    UpdateField(field, style);
                };
                colorsLayout.Add(italic);

                colorsLayout.EndBeginHorizontal();

                colorsLayout.Add(new Label { Text = "Bold" });
                var bold = new CheckBox { Checked = (style.FontStyle & FontStyle.Bold) != 0 };
                bold.CheckedChanged += (_, _) => {
                    if (bold.Checked is bool b) {
                        if (b) {
                            style.FontStyle |= FontStyle.Bold;
                        } else {
                            style.FontStyle &= ~FontStyle.Bold;
                        }
                    }
                    UpdateField(field, style);
                };
                colorsLayout.Add(bold);

                colorsLayout.EndHorizontal();
                colorsLayout.EndVertical();
                */
            }

            fieldsLayout.EndHorizontal();
        }
        fieldsLayout.Create();
    }

    private void UpdateField(FieldInfo field, object value) {
        if (IsBuiltin()) {
            return;
        }
        object theme = Settings.Instance.Theme;
        field.SetValue(theme, value);
        Settings.Instance.CustomThemes[Settings.Instance.ThemeName] = (Theme) theme;
        Settings.OnThemeChanged();
        Settings.Save();
    }
    
    private static string? AskString(string text, string initialValue) {
        var dialog = new Dialog<string?>();

        var input = new TextBox { Text = initialValue };
        input.SelectAll();

        dialog.DefaultButton = new Button((_, _) => dialog.Close(input.Text)) { Text = "Confirm" };
        dialog.AbortButton = new Button((_, _) => dialog.Close(null)) { Text = "Cancel" };
        dialog.PositiveButtons.Add(dialog.DefaultButton);
        dialog.NegativeButtons.Add(dialog.AbortButton);

        dialog.Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items = {
                new Label { Text = text, TextAlignment = TextAlignment.Center },
                input,
            }
        };
        
        dialog.Load += (_, _) => Studio.Instance.WindowCreationCallback(dialog);
        dialog.Shown += (_, _) => dialog.Location = Studio.Instance.Location + new Point((Studio.Instance.Width - dialog.Width) / 2, (Studio.Instance.Height - dialog.Height) / 2);

        return dialog.ShowModal();
    }
}