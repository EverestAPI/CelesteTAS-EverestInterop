using System;
using System.Diagnostics;
using System.Numerics;
using CelesteStudio.Dialog;
using Eto.Forms;

namespace CelesteStudio.Util;

public class MenuUtils {
    public static MenuItem CreateAction(string text, Keys shortcut = Keys.None, Action? action = null) {
        return new ButtonMenuItem((_, _) => action?.Invoke()) { Text = text, Shortcut = shortcut, Enabled = action != null };
    }
    
    public static MenuItem CreateToggle(string text, Func<bool> getFn, Action toggleFn) {
        // TODO: Convert to CheckMenuItem
        return new ButtonMenuItem((_, _) => toggleFn()) { Text = text };
    }
    
    public static MenuItem CreateSettingToggle(string text, string settingName, Keys shortcut = Keys.None, Action<bool>? onChanged = null) {
        var property = typeof(Settings).GetProperty(settingName)!;
        
        var item = new CheckMenuItem {
            Text = text,
            Shortcut = shortcut,
            Checked = (bool)property.GetValue(Settings.Instance)!
        };
        item.Click += (_, _) => {
            bool value = (bool)property.GetValue(Settings.Instance)!;
            property.SetValue(Settings.Instance, !value);
            onChanged?.Invoke(!value);
            
            Settings.OnChanged();
            Settings.Save();
            
        };
        
        return item;
    }

    public static MenuItem CreateFeatherlineSettingToggle(string text, string settingName) {
        var property = typeof(FeatherlineSettings).GetProperty(settingName)!;

        var item = new CheckMenuItem {
            Text = text,
            Checked = (bool)property.GetValue(FeatherlineSettings.Instance)!
        };
        item.Click += (_, _) => {
            bool value = (bool)property.GetValue(FeatherlineSettings.Instance)!;
            property.SetValue(FeatherlineSettings.Instance, !value);

            FeatherlineSettings.OnChanged();
            FeatherlineSettings.Save();
        };

        return item;
    }

    public static MenuItem CreateNumberInput<T>(string text, Func<T> getFn, Action<T> setFn, T minValue, T maxValue, T step) where T : INumber<T> {
        return new ButtonMenuItem((_, _) => setFn(NumberInputDialog<T>.Show(text, getFn(), minValue, maxValue, step))) { Text = text };
    }

    public static MenuItem CreateSettingNumberInput<T>(string text, string settingName, T minValue, T maxValue, T step) where T : INumber<T>  {
        var property = typeof(Settings).GetProperty(settingName)!;
        
        var item = new ButtonMenuItem {
            Text = $"{text}: {property.GetValue(Settings.Instance)!}"
        };
        item.Click += (_, _) => {
            T value = (T)property.GetValue(Settings.Instance)!;
            property.SetValue(Settings.Instance, NumberInputDialog<T>.Show(text, value, minValue, maxValue, step));
            
            Settings.OnChanged();
            Settings.Save();
        };
        
        return item;
    }

    public static MenuItem CreateFeatherlineSettingNumberInput<T>(string text, string settingName, T minValue, T maxValue, T step) where T : INumber<T> {
        var property = typeof(FeatherlineSettings).GetProperty(settingName)!;

        var item = new ButtonMenuItem {
            Text = $"{text}: {property.GetValue(FeatherlineSettings.Instance)!}"
        };
        item.Click += (_, _) => {
            T value = (T) property.GetValue(FeatherlineSettings.Instance)!;
            property.SetValue(FeatherlineSettings.Instance, NumberInputDialog<T>.Show(text, value, minValue, maxValue, step));

            FeatherlineSettings.OnChanged();
            FeatherlineSettings.Save();
        };

        return item;
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
                
                Settings.OnChanged();
                Settings.Save();
            };
        }
        
        return selector;
    }
}