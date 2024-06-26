using System;
using System.Numerics;
using Eto.Forms;

namespace CelesteStudio.Util;

public class MenuUtils {
    public static MenuItem CreateAction(string text, Keys shortcut = Keys.None, Action? action = null) {
        var cmd = new Command { MenuText = text, Shortcut = shortcut, Enabled = action != null };
        cmd.Executed += (_, _) => action?.Invoke();
        
        return cmd;
    }
    
    public static MenuItem CreateToggle(string text, Func<bool> getFn, Action toggleFn) {
        var cmd = new CheckCommand { MenuText = text };
        cmd.Executed += (_, _) => toggleFn();
        
        // TODO: Convert to CheckMenuItem
        return new ButtonMenuItem(cmd);
    }
    
    public static MenuItem CreateSettingToggle(string text, string settingName, Keys shortcut = Keys.None) {
        var property = typeof(Settings).GetField(settingName)!;
        
        var cmd = new CheckCommand {
            MenuText = text,
            Shortcut = shortcut,
            Checked = (bool)property.GetValue(Settings.Instance)!
        };
        cmd.Executed += (_, _) => {
            bool value = (bool)property.GetValue(Settings.Instance)!;
            property.SetValue(Settings.Instance, !value);
            
            Settings.Instance.OnChanged();
            Settings.Save();
        };
        
        return new CheckMenuItem(cmd);
    }
    
    public static MenuItem CreateNumberInput<T>(string text, Func<T> getFn, Action<T> setFn, T minValue, T maxValue, T step) where T : INumber<T> {
        var cmd = new Command { MenuText = text };
        cmd.Executed += (_, _) => setFn(DialogUtil.ShowNumberInputDialog(text, getFn(), minValue, maxValue, step));
        
        return new ButtonMenuItem(cmd);
    }
    
    public static MenuItem CreateSettingNumberInput<T>(string text, string settingName, T minValue, T maxValue, T step) where T : INumber<T>  {
        var property = typeof(Settings).GetField(settingName)!;
        
        var cmd = new Command { MenuText = $"{text}: {property.GetValue(Settings.Instance)!}" };
        cmd.Executed += (_, _) => {
            T value = (T)property.GetValue(Settings.Instance)!;
            property.SetValue(Settings.Instance, DialogUtil.ShowNumberInputDialog(text, value, minValue, maxValue, step));
            
            Settings.Instance.OnChanged();
            Settings.Save();
        };
        
        return new ButtonMenuItem(cmd);
    }
}