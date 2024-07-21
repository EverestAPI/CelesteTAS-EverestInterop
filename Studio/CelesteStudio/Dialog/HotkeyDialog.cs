using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CelesteStudio.Data;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class HotkeyDialog : Dialog<Keys> {
    private HotkeyDialog(Keys currentHotkey, Dictionary<MenuEntry, Keys> keyBindings, List<Snippet> snippets) {
        Title = "Edit Hotkey";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items = {
                new Label { Text = "Press any key...", Font = SystemFonts.Bold().WithFontStyle(FontStyle.Bold | FontStyle.Italic) },
                new Label { Text = $"Press {(Application.Instance.CommonModifier | Keys.Escape).ToShortcutString()} to clear", Font = SystemFonts.Label() }
            }
        };
        Icon = Assets.AppIcon;
        
        Result = currentHotkey;

        KeyDown += (_, e) => {
            // Don't allow binding modifiers by themselves
            if (e.Key is Keys.LeftShift or Keys.RightShift
                or Keys.LeftControl or Keys.RightControl
                or Keys.LeftAlt or Keys.RightAlt
                or Keys.LeftApplication or Keys.RightApplication)
            {
                return;
            }
            
            if (e.KeyData == (Application.Instance.CommonModifier | Keys.Escape)) {
                Close(Keys.None);
                return;
            } else if (e.KeyData == currentHotkey) {
                Close();
                return;
            }
                
            // Avoid conflicts with other hotkeys
            var conflictingKeyBinds = keyBindings.Where(pair => pair.Value == e.KeyData).Select(pair => pair.Key).ToArray();
            var conflictingSnippets = snippets.Where(snippet => snippet.Hotkey == e.KeyData).ToArray();
            
            if (conflictingKeyBinds.Any() || conflictingSnippets.Any()) {
                var msg = new StringBuilder();
                msg.AppendLine($"This hotkey ({e.KeyData.ToShortcutString()}) is already used for other key bindings / snippets!");
                if (conflictingKeyBinds.Any()) {
                    msg.AppendLine("The following key bindings already use this hotkey:");
                    foreach (var conflict in conflictingKeyBinds) {
                        msg.AppendLine($"    - {conflict.GetName().Replace("&", string.Empty)}");
                    }
                    msg.AppendLine(string.Empty);
                }
                if (conflictingSnippets.Any()) {
                    msg.AppendLine("The following snippets already use this hotkey:");
                    foreach (var conflict in conflictingSnippets) {
                        var lines = conflict.Insert.ReplaceLineEndings(Document.NewLine.ToString()).Split(Document.NewLine);
                        var shortcut = !string.IsNullOrWhiteSpace(conflict.Shortcut) ? $"'{conflict.Shortcut}' = " : "";
                        var insert = lines[0] + (lines.Length > 1 ? "..." : string.Empty);
                        msg.AppendLine($"    - {shortcut}'{insert}'");
                    }
                    msg.AppendLine(string.Empty);
                }
                msg.AppendLine("Are you sure you want to use this hotkey?");
                
                var confirm = MessageBox.Show(msg.ToString(), MessageBoxButtons.YesNo, MessageBoxType.Question, MessageBoxDefaultButton.Yes);
                if (confirm != DialogResult.Yes) {
                    return;
                }
            }
            
            if (e.KeyData == (Application.Instance.CommonModifier | Keys.Escape)) {
                Close(Keys.None);
            } else {
                Close(e.KeyData);
            }
        };
        
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = ParentWindow.Location + new Point((ParentWindow.Width - Width) / 2, (ParentWindow.Height - Height) / 2);
    }

    public static Keys Show(Window parent, Keys currentHotkey, Dictionary<MenuEntry, Keys>? keyBindings, List<Snippet>? snippets) {
        keyBindings ??= Enum.GetValues<MenuEntry>().ToDictionary(entry => entry, entry => entry.GetHotkey());
        snippets ??= Settings.Instance.Snippets;
        
        return new HotkeyDialog(currentHotkey, keyBindings, snippets).ShowModal(parent);
    }
}