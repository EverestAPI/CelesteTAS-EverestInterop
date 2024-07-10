using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using CelesteStudio.Editing;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class Extensions
{
    public static string[] SplitDocumentLines(this string self, StringSplitOptions options = StringSplitOptions.None) => self.Split(Document.NewLine, options);

    public static int Digits(this int self) => Math.Abs(self).ToString().Length;
    public static T Mod<T>(this T x, T m) where T : INumber<T> => (x % m + m) % m; 
    
    public static string ReplaceRange(this string self, int startIndex, int count, string replacement) => self.Remove(startIndex, count).Insert(startIndex, replacement);
    
    public static Font WithFontStyle(this Font font, FontStyle style) => new(font.Family, font.Size, style);
    public static Font WithFontDecoration(this Font font, FontDecoration decoration) => new(font.Family, font.Size, font.FontStyle, decoration);
    
    public static CommonControl WithFontStyle(this CommonControl self, FontStyle style) {
        self.Font = self.Font.WithFontStyle(style);
        return self;
    }
    
    public static string HotkeyToString(this Keys hotkey, string separator) {
        var keys = new List<Keys>();
        if (hotkey.HasFlag(Keys.Application))
            keys.Add(Keys.Application);
        if (hotkey.HasFlag(Keys.Control))
            keys.Add(Keys.Control);
        if (hotkey.HasFlag(Keys.Alt))
            keys.Add(Keys.Alt);
        if (hotkey.HasFlag(Keys.Shift))
            keys.Add(Keys.Shift);
        keys.Add(hotkey & Keys.KeyMask);
        
        return string.Join(separator, keys);
    }
    
    public static Keys HotkeyFromString(this string hotkeyString, string separator) {
        var keys = hotkeyString
                .Split(separator)
                .Select(Enum.Parse<Keys>)
                .ToArray();
        
        var hotkey = keys.FirstOrDefault(key => (key & Keys.KeyMask) != Keys.None, Keys.None);
        if (hotkey == Keys.None)
            return Keys.None;
        
        if (keys.Any(key => key == Keys.Application))
            hotkey |= Keys.Application;
        if (keys.Any(key => key == Keys.Control))
            hotkey |= Keys.Control;
        if (keys.Any(key => key == Keys.Alt))
            hotkey |= Keys.Alt;
        if (keys.Any(key => key == Keys.Shift))
            hotkey |= Keys.Shift;
        
        return hotkey;
    }
    
    public static bool HasCommonModifier(this Keys keys) => keys.HasFlag(Application.Instance.CommonModifier);
    public static bool HasAlternateModifier(this Keys keys) => keys.HasFlag(Application.Instance.AlternateModifier);
    public static bool HasCommonModifier(this KeyEventArgs e) => e.Modifiers.HasFlag(Application.Instance.CommonModifier);
    public static bool HasAlternateModifier(this KeyEventArgs e) => e.Modifiers.HasFlag(Application.Instance.AlternateModifier);
    public static bool HasCommonModifier(this MouseEventArgs e) => e.Modifiers.HasFlag(Application.Instance.CommonModifier);
    public static bool HasAlternateModifier(this MouseEventArgs e) => e.Modifiers.HasFlag(Application.Instance.AlternateModifier);
    
    public static int IndexOf<T>(this IEnumerable<T> obj, T value) => obj.IndexOf(value, EqualityComparer<T>.Default);
    public static int IndexOf<T>(this IEnumerable<T> obj, T value, IEqualityComparer<T> comparer) {
        using var iter = obj.GetEnumerator();
        
        int i = 0;
        while (iter.MoveNext()) {
            if (comparer.Equals(iter.Current, value))
                return i;
            i++;
        }
        
        return -1;
    }
    
    // Stolen from https://stackoverflow.com/a/36845864
    public static int GetStableHashCode(this string str)
    {
        unchecked {
            int hash1 = 5381;
            int hash2 = hash1;
            
            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2) {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1 || str[i+1] == '\0') {
                    break;
                }
                hash2 = ((hash2 << 5) + hash2) ^ str[i+1];
            }
            
            return hash1 + (hash2*1566083941);
        }
    }
}