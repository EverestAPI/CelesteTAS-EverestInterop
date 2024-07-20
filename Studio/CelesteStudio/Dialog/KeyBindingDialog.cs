using System;
using System.Collections.Generic;
using CelesteStudio.Data;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class KeyBindingDialog : Dialog<bool> {
    private readonly Dictionary<MenuEntry, Keys> keyBindings = new();
    
    private KeyBindingDialog() {
        var list = new StackLayout {
            MinimumSize = new Size(500, 0),
            Padding = 10,
            Spacing = 10,
        };
        
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
            
            var box = new GroupBox {
                Text = category.GetName(),
                Content = layout,
                Padding = 10,
            };
            list.Items.Add(box);
        }
        
        Title = "Edit Key Bindings";
        Content = new Scrollable {
            Padding = 10,
            Width = list.Width,
            Height = 500,
            Content = list,
        }.FixBorder();
        Icon = Assets.AppIcon;
        
        DefaultButton = new Button((_, _) => Close(true)) { Text = "&OK" };
        AbortButton = new Button((_, _) => Close(false)) { Text = "&Cancel" };
        
        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);
        
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
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
        
        Settings.OnKeyBindingsChanged();
        Settings.Save();
    }
}