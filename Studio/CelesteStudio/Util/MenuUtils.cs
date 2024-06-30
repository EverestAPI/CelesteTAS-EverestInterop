using System;
using System.Diagnostics;
using System.Numerics;
using CelesteStudio.Dialog;
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
        var property = typeof(Settings).GetProperty(settingName)!;
        
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
        cmd.Executed += (_, _) => setFn(NumberInputDialog<T>.Show(text, getFn(), minValue, maxValue, step));
        
        return new ButtonMenuItem(cmd);
    }
    
    public static MenuItem CreateSettingNumberInput<T>(string text, string settingName, T minValue, T maxValue, T step) where T : INumber<T>  {
        var property = typeof(Settings).GetProperty(settingName)!;
        
        var cmd = new Command { MenuText = $"{text}: {property.GetValue(Settings.Instance)!}" };
        cmd.Executed += (_, _) => {
            T value = (T)property.GetValue(Settings.Instance)!;
            property.SetValue(Settings.Instance, NumberInputDialog<T>.Show(text, value, minValue, maxValue, step));
            
            Settings.Instance.OnChanged();
            Settings.Save();
        };
        
        return new ButtonMenuItem(cmd);
    }
    
    public static MenuItem CreateSettingEnum<T>(string text, string settingName, string[] entryNames) where T : struct, Enum {
        var property = typeof(Settings).GetProperty(settingName)!;
        var values = Enum.GetValues<T>();
        Debug.Assert(typeof(T) == property.PropertyType);
        Debug.Assert(entryNames.Length == values.Length);
        
        var selector = new SubMenuItem { Text = text };
        
        var controller = new RadioMenuItem { Text = entryNames[0] };
        selector.Items.Add(controller);
        foreach (var entry in entryNames[1..]) {
            selector.Items.Add(new RadioMenuItem(controller) { Text = entry });
        }
        
        var currentValue = (T)property.GetValue(Settings.Instance)!;
        for (int i = 0; i < selector.Items.Count; i++) {
            var item = (RadioMenuItem)selector.Items[i];
            var value = values[i];
            
            item.Checked = currentValue.Equals(value);
            item.Click += (_, _) => {
                property.SetValue(Settings.Instance, value);
                
                Settings.Instance.OnChanged();
                Settings.Save();
            };
        }
        
        return selector;
    }
}