using System;
using System.Collections.Generic;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using System.Linq;
using Binding = CelesteStudio.Binding;

namespace CelesteStudio.Dialog;

public class KeyBindingDialog : Dialog<bool> {
    private readonly Dictionary<Binding.Entry, Hotkey> keyBindings = new();

    private KeyBindingDialog() {
        var list = new StackLayout {
            MinimumSize = new Size(500, 0),
            Padding = 10,
            Spacing = 10,
        };

        const int labelWidth = 300;
        const int buttonWidth = 150;

        foreach (var category in Enum.GetValues<Binding.Category>()) {
            var layout = new DynamicLayout {
                DefaultSpacing = new Size(15, 5),
                Padding = new Padding(0, 0, 0, 10),
            };
            layout.BeginVertical();
            foreach (var binding in Studio.GetAllStudioBindings().Where(b => b.DisplayCategory == category)) {
                if (binding.Entries.Length == 1) {
                    var entry = binding.Entries[0];
                    var hotkey = Settings.Instance.KeyBindings.GetValueOrDefault(entry.Identifier, entry.DefaultHotkey);
                    keyBindings.Add(entry, hotkey);

                    var hotkeyButton = new Button {
                        Text = hotkey.ToShortcutString(),
                        Font = SystemFonts.Bold(),
                        Width = buttonWidth,
                    };
                    hotkeyButton.Click += (_, _) => {
                        hotkey = HotkeyDialog.Show(this, hotkey, keyBindings, null, entry.PreferTextHotkey);

                        keyBindings[entry] = hotkey;
                        hotkeyButton.Text = hotkey.ToShortcutString();
                    };

                    layout.Add(new StackLayout {
                        Orientation = Orientation.Horizontal,
                        Items = { new Label { Text = entry.DisplayName, Width = labelWidth }, hotkeyButton }
                    });
                } else {
                    var subLayout = new DynamicLayout {
                        DefaultSpacing = new Size(15, 5),
                        Padding = new Padding(15, 0, 0, 10),
                    };
                    subLayout.BeginVertical();

                    foreach (var entry in binding.Entries) {
                        subLayout.BeginHorizontal();

                        var hotkey = Settings.Instance.KeyBindings.GetValueOrDefault(entry.Identifier, entry.DefaultHotkey);
                        keyBindings.Add(entry, hotkey);

                        var hotkeyButton = new Button {
                            Text = hotkey.ToShortcutString(),
                            Font = SystemFonts.Bold(),
                            Width = buttonWidth,
                        };
                        hotkeyButton.Click += (_, _) => {
                            hotkey = HotkeyDialog.Show(this, hotkey, keyBindings, null, entry.PreferTextHotkey);

                            keyBindings[entry] = hotkey;
                            hotkeyButton.Text = hotkey.ToShortcutString();
                        };

                        subLayout.BeginVertical();
                        subLayout.AddSpace();
                        subLayout.Add(new Label { Text = entry.DisplayName, Width = labelWidth - 30 });
                        subLayout.AddSpace();
                        subLayout.EndVertical();

                        subLayout.Add(hotkeyButton);

                        subLayout.EndHorizontal();
                    }

                    subLayout.EndVertical();

                    layout.Add(new Expander { Header = binding.DisplayName, Content = subLayout, Height = 40 });
                }
            }
            layout.EndVertical();

            list.Items.Add(new GroupBox {
                Text = category switch {
                    Binding.Category.File => "File",
                    Binding.Category.Settings => "Settings",
                    Binding.Category.View => "View",
                    Binding.Category.Editor => "Editor",
                    Binding.Category.FrameOperations => "Frame Operations",
                    Binding.Category.ContextActions => "Context Actions",
                    Binding.Category.Status => "Game Info",
                    Binding.Category.StatusPopout => "Game Info Popout",
                    Binding.Category.Game => "Additional Game Hotkeys",
                    _ => throw new ArgumentOutOfRangeException()
                },
                Content = layout,
                Padding = 10,
            });
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
        if (!dialog.ShowModal()) {
            return;
        }

        // Only save non-default hotkeys
        Settings.Instance.KeyBindings.Clear();
        foreach (var (entry, hotkey) in dialog.keyBindings) {
            if (entry.DefaultHotkey != hotkey) {
                Settings.Instance.KeyBindings[entry.Identifier] = hotkey;
            }
        }

        Settings.OnKeyBindingsChanged();
        Settings.Save();
    }
}
