using System;
using System.Collections.Generic;
using CelesteStudio.Data;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class KeyBindingDialog : Dialog<bool> {
    private readonly Dictionary<MenuEntry, Keys> keyBindings = new();

    private char addFrameOp, subFrameOp, mulFrameOp, divFrameOp, setFrameOp;

    private KeyBindingDialog() {
        var list = new StackLayout {
            MinimumSize = new Size(500, 0),
            Padding = 10,
            Spacing = 10,
        };

        addFrameOp = Settings.Instance.AddFrameOperationChar;
        subFrameOp = Settings.Instance.SubFrameOperationChar;
        mulFrameOp = Settings.Instance.MulFrameOperationChar;
        divFrameOp = Settings.Instance.DivFrameOperationChar;
        setFrameOp = Settings.Instance.SetFrameOperationChar;

        foreach (var category in Enum.GetValues<MenuEntryCategory>()) {
            var layout = new DynamicLayout {
                DefaultSpacing = new Size(15, 5),
                Padding = new Padding(0, 0, 0, 10),
            };
            layout.BeginVertical();
            foreach (var entry in category.GetEntries()) {
                layout.BeginHorizontal();

                var hotkey = entry.GetHotkey();
                keyBindings.Add(entry, hotkey);

                var hotkeyButton = new Button {
                    Text = hotkey.ToShortcutString(),
                    ToolTip = "Use the right mouse button to clear a hotkey!",
                    Font = SystemFonts.Bold(),
                    Width = 150,
                };
                hotkeyButton.Click += (_, _) => {
                    hotkey = HotkeyDialog.Show(this, hotkey, keyBindings, null);

                    keyBindings[entry] = hotkey;
                    hotkeyButton.Text = hotkey.ToShortcutString();
                };

                layout.BeginVertical();
                layout.AddSpace();
                layout.Add(new Label { Text = entry.GetName(), Width = 300 });
                layout.AddSpace();
                layout.EndVertical();

                layout.Add(hotkeyButton);

                layout.EndHorizontal();
            }
            layout.EndVertical();

            list.Items.Add(new GroupBox {
                Text = category.GetName(),
                Content = layout,
                Padding = 10,
            });

            // Append custom "Frame Operations" settings
            if (category == MenuEntryCategory.Editor) {
                var frameOpLayout = new DynamicLayout {
                    DefaultSpacing = new Size(15, 5),
                    Padding = new Padding(0, 0, 0, 10),
                };
                frameOpLayout.BeginVertical();

                foreach (var op in Enum.GetValues<CalculationOperator>()) {
                    frameOpLayout.BeginHorizontal();

                    var charBox = new TextBox {
                        Font = SystemFonts.Bold(),
                        Width = 150,

                        MaxLength = 1,
                        Text = op switch {
                            CalculationOperator.Add => addFrameOp == char.MaxValue ? string.Empty : addFrameOp.ToString(),
                            CalculationOperator.Sub => subFrameOp == char.MaxValue ? string.Empty : subFrameOp.ToString(),
                            CalculationOperator.Mul => mulFrameOp == char.MaxValue ? string.Empty : mulFrameOp.ToString(),
                            CalculationOperator.Div => divFrameOp == char.MaxValue ? string.Empty : divFrameOp.ToString(),
                            CalculationOperator.Set => setFrameOp == char.MaxValue ? string.Empty : setFrameOp.ToString(),
                            _ => throw new ArgumentOutOfRangeException()
                        },
                    };
                    charBox.TextChanged += (_, _) => {
                        char c = charBox.Text.Length == 0 ? char.MaxValue : char.ToUpper(charBox.Text[0]);

                        string text = c == char.MaxValue ? string.Empty : c.ToString();
                        if (charBox.Text != text) {
                            charBox.Text = text;
                        }

                        switch (op) {
                            case CalculationOperator.Add:
                                addFrameOp = c;
                                break;
                            case CalculationOperator.Sub:
                                subFrameOp = c;
                                break;
                            case CalculationOperator.Mul:
                                mulFrameOp = c;
                                break;
                            case CalculationOperator.Div:
                                divFrameOp = c;
                                break;
                            case CalculationOperator.Set:
                                setFrameOp = c;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    };

                    frameOpLayout.BeginVertical();
                    frameOpLayout.AddSpace();
                    frameOpLayout.Add(new Label { Text = op switch {
                        CalculationOperator.Add => "Add",
                        CalculationOperator.Sub => "Subtract",
                        CalculationOperator.Mul => "Multiply",
                        CalculationOperator.Div => "Divide",
                        CalculationOperator.Set => "Set",
                        _ => throw new ArgumentOutOfRangeException()
                    }, Width = 300 });
                    frameOpLayout.AddSpace();
                    frameOpLayout.EndVertical();

                    frameOpLayout.Add(charBox);

                    frameOpLayout.EndHorizontal();
                }

                list.Items.Add(new GroupBox {
                    Text = "Frame Operations",
                    Content = frameOpLayout,
                    Padding = 10,
                });
            }
        }

        Title = "Edit Key Bindings";
        Content = new Scrollable {
            Padding = 10,
            Width = list.Width,
            Height = 500,
            Content = list,
        }.FixBorder();

        DefaultButton = new Button((_, _) => Close(true)) { Text = "&OK" };
        AbortButton = new Button((_, _) => Close(false)) { Text = "&Cancel" };

        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);

        Studio.RegisterDialog(this);
    }

    public static void Show() {
        var dialog = new KeyBindingDialog();
        if (!dialog.ShowModal())
            return;

        // Only save non-default hotkeys
        Settings.Instance.KeyBindings.Clear();
        foreach (var (entry, hotkey) in dialog.keyBindings) {
            if (entry.GetDefaultHotkey() != hotkey) {
                Settings.Instance.KeyBindings[entry] = hotkey;
            }
        }

        Settings.Instance.AddFrameOperationChar = dialog.addFrameOp;
        Settings.Instance.SubFrameOperationChar = dialog.subFrameOp;
        Settings.Instance.MulFrameOperationChar = dialog.mulFrameOp;
        Settings.Instance.DivFrameOperationChar = dialog.divFrameOp;
        Settings.Instance.SetFrameOperationChar = dialog.setFrameOp;

        Settings.OnKeyBindingsChanged();
        Settings.Save();
    }
}
