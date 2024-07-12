using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Reflection;
using CelesteStudio.Editing;

namespace CelesteStudio;

public sealed class ThemeEditor : Form {
    private readonly DropDown selector;
    private DynamicLayout colorsLayout;
    private readonly CheckBox darkModeCheckBox;

    // Due to a bug(?) in the GTK impl, ColorPickers have to be manually Disposed
    // see https://github.com/picoe/Eto/issues/2664 for more details
    private readonly List<ColorPicker> pickers = [];

    public ThemeEditor() {
        Title = "Theme Editor";
        Icon = Studio.Instance.Icon;

        var layout = new DynamicLayout { DefaultSpacing = new Size(10, 10), Padding = new Padding(10) };
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
            if (selector.SelectedKey != Settings.Instance.ThemeName) {
                Settings.Instance.ThemeName = selector.SelectedKey;
                InitializeThemeParameters();
            }
        };
        Settings.ThemeChanged += () => {
            selector.SelectedKey = Settings.Instance.ThemeName;
            InitializeThemeParameters();
        };
        layout.AddAutoSized(selector);

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

        colorsLayout = new DynamicLayout { DefaultSpacing = new Size(10, 10), Padding = new Padding(15) };
        layout.Add(colorsLayout);

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

    private void Delete() {
        if (IsBuiltin()) {
            MessageBox.Show("Cannot delete a builtin theme", MessageBoxType.Error);
            return;
        }

        var prevName = Settings.Instance.ThemeName;
        if (MessageBox.Show($"Delete {prevName}?",
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

    private void Duplicate() {
        string? newName_ = AskString("Name for the new theme:", Settings.Instance.ThemeName);
        if (String.IsNullOrWhiteSpace(newName_)) {
            return;
        }
        string newName = newName_!.Trim();
        if (Settings.Instance.CustomThemes.ContainsKey(newName)) {
            MessageBox.Show($"'{newName}' already exists", MessageBoxType.Error);
            return;
        }

        selector.Items.Add(new ListItem { Text = newName, Key = newName });
        Settings.Instance.CustomThemes[newName] = Settings.Instance.Theme;
        Settings.Instance.ThemeName = newName;

        Settings.OnChanged();
        Settings.Save();
    }

    private void InitializeThemeParameters() {
        // Non-color settings
        darkModeCheckBox.Enabled = !IsBuiltin();
        darkModeCheckBox.Checked = Settings.Instance.Theme.DarkMode;

        // Color pickers
        colorsLayout.Clear();
        
        // See the comment on `pickers`
        foreach (var picker in pickers) {
            picker.Dispose();
        }
        pickers.Clear();
        if (IsBuiltin()) {
            return;
        }

        foreach (var field in typeof(Theme).GetFields()) {
            colorsLayout.BeginHorizontal();
            var value = field.GetValue(Settings.Instance.Theme);

            if (value is Color color) {
                colorsLayout.Add(new Label { Text = field.Name });

                var picker = new ColorPicker { Value = color, AllowAlpha = true };
                picker.ValueChanged += (_, _) => {
                    UpdateField(field, picker.Value);
                };
                colorsLayout.Add(picker);
                pickers.Add(picker);

            } else if (value is Style style) {
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
            }

            colorsLayout.EndHorizontal();
        }
        colorsLayout.Create();
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