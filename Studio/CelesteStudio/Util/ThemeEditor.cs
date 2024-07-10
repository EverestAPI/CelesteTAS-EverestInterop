using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Reflection;
using CelesteStudio.Editing;

namespace CelesteStudio.Util;

public sealed class ThemeEditor : Form {
    private DropDown selector;
    private DynamicLayout colorsLayout;
    // Due to a bug(?) in the GTK impl, ColorPickers have to be manually Disposed
    // see https://github.com/picoe/Eto/issues/2664 for more details
    private List<ColorPicker> pickers = [];

    public ThemeEditor() {
        Title = "Celeste Studio - Theme Editor";
        Icon = Studio.Instance.Icon;

        var layout = new DynamicLayout {
            DefaultSpacing = new Size(10, 10),
            Padding = new Padding(10),
        };
        layout.BeginHorizontal();
        // left side
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
                InitializePickers();
            }
        };
        Settings.ThemeChanged += () => {
            selector.SelectedKey = Settings.Instance.ThemeName;
            InitializePickers();
        };
        layout.AddAutoSized(selector);

        var duplicate = new Button { Text = "Duplicate" };
        duplicate.Click += (_, _) => {
            Duplicate();
        };
        layout.Add(duplicate);

        var delete = new Button { Text = "Delete" };
        delete.Click += (_, _) => {
            Delete();
        };
        layout.Add(delete);

        layout.AddSpace();
        layout.EndVertical();

        // right side
        layout.BeginVertical();
        layout.BeginScrollable();

        colorsLayout = new DynamicLayout {
            DefaultSpacing = new Size(5, 5),
            Padding = new Padding(5),
        };
        layout.Add(colorsLayout);

        InitializePickers();

        layout.EndScrollable();
        layout.EndVertical();

        layout.EndHorizontal();

        Content = layout;
        Resizable = true;
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
        if (MessageBox.Show("Delete " + prevName + "?",
                    MessageBoxButtons.YesNo,
                    MessageBoxType.Question,
                    MessageBoxDefaultButton.No) != DialogResult.Yes) {
            return;
        }

        // select next
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
            MessageBox.Show(newName + " already exists", MessageBoxType.Error);
            return;
        }

        selector.Items.Add(new ListItem { Text = newName, Key = newName });
        Settings.Instance.CustomThemes[newName] = Settings.Instance.Theme;
        Settings.Instance.ThemeName = newName;

        Settings.OnChanged();
        Settings.Save();
    }

    private void InitializePickers() {
        colorsLayout.Clear();
        // see the comment on `pickers`
        foreach (var picker in pickers) {
            picker.Dispose();
        }
        pickers.Clear();
        if (IsBuiltin()) {
            return;
        }
        var fields = typeof(Theme).GetFields();
        foreach (var field in fields) {
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
        object theme = Settings.Instance.Theme;
        field.SetValue(theme, value);
        Settings.Instance.CustomThemes[Settings.Instance.ThemeName] = (Theme) theme;
        Settings.OnThemeChanged();
        Settings.Save();
    }

    public static String? AskString(string text, string initialValue) {
        String? result = null;
        var dialog = new Eto.Forms.Dialog();

        var input = new TextBox() { Text = initialValue };
        input.SelectAll();

        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Click += (_, _) => {
            dialog.Close();
        };
        var confirmButton = new Button { Text = "Confirm" };
        confirmButton.Click += (_, _) => {
            result = input.Text;
            dialog.Close();
        };
        dialog.AbortButton = cancelButton;
        dialog.DefaultButton = confirmButton;
        dialog.PositiveButtons.Add(confirmButton);
        dialog.NegativeButtons.Add(cancelButton);

        dialog.Content = new StackLayout {
            Padding = 10,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items = {
                new Label { Text = text, TextAlignment = TextAlignment.Center },
                input,
            }
        };

        dialog.ShowModal();
        return result;
    }
}