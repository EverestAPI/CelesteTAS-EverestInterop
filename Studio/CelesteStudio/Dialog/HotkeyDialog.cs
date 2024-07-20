using System;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Dialog;

public class HotkeyDialog : Dialog<Keys> {
    private readonly Func<Keys, bool>? checkValid;
    
    private HotkeyDialog(Keys currentHotkey, Func<Keys, bool>? checkValid) {
        this.checkValid = checkValid;
        
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
        
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = ParentWindow.Location + new Point((ParentWindow.Width - Width) / 2, (ParentWindow.Height - Height) / 2);
    }
    
    protected override void OnKeyDown(KeyEventArgs e) {
        // Don't allow binding modifiers by themselves
        if (e.Key is Keys.LeftShift or Keys.RightShift
            or Keys.LeftControl or Keys.RightControl
            or Keys.LeftAlt or Keys.RightAlt
            or Keys.LeftApplication or Keys.RightApplication) 
        {
            return;
        }
        
        if (checkValid != null && !checkValid(e.KeyData)) {
            return;
        }
        
        if (e.KeyData == (Application.Instance.CommonModifier | Keys.Escape)) {
            Close(Keys.None);
        } else {
            Close(e.KeyData);
        }
    }
    
    public static Keys Show(Window parent, Keys currentHotkey, Func<Keys, bool>? checkValid = null) {
        return new HotkeyDialog(currentHotkey, checkValid).ShowModal(parent);
    }
}