using System;
using System.Collections.Generic;
using CelesteStudio.Editing;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using System.Reflection;
using Range = System.Range;

namespace CelesteStudio.Util;

public static class Extensions
{
    /// Helper method to modify a value without requiring a variable
    public static T Apply<T>(this T obj, Action<T> action) {
        action(obj);
        return obj;
    }

    public static string[] SplitDocumentLines(this string self, StringSplitOptions options = StringSplitOptions.None) => self.Split(Document.NewLine, options);

    public static int Digits(this int self) => Math.Abs(self).ToString().Length;

    public static T[] GetArrayRange<T>(this IReadOnlyList<T> list, Range range) {
        var (start, length) = range.GetOffsetAndLength(list.Count);
        var result = new T[length];

        if (list is List<T> listImpl) {
            // Fast-path for regular lists
            listImpl.CopyTo(start, result, 0, length);
        } else {
            for (int i = 0; i < length; i++) {
                result[i] = list[start + i];
            }
        }

        return result;
    }

    public static Font WithFontStyle(this Font font, FontStyle style) => new(font.Family, font.Size, style);
    public static Font WithFontDecoration(this Font font, FontDecoration decoration) => new(font.Family, font.Size, font.FontStyle, decoration);

    public static CommonControl WithFontStyle(this CommonControl self, FontStyle style) {
        self.Font = self.Font.WithFontStyle(style);
        return self;
    }

    public static bool HasCommonModifier(this Keys keys) => keys.HasFlag(Application.Instance.CommonModifier);
    public static bool HasAlternateModifier(this Keys keys) => keys.HasFlag(Application.Instance.AlternateModifier);
    public static bool HasCommonModifier(this KeyEventArgs e) => e.Modifiers.HasFlag(Application.Instance.CommonModifier);
    public static bool HasAlternateModifier(this KeyEventArgs e) => e.Modifiers.HasFlag(Application.Instance.AlternateModifier);
    public static bool HasCommonModifier(this MouseEventArgs e) => e.Modifiers.HasFlag(Application.Instance.CommonModifier);
    public static bool HasAlternateModifier(this MouseEventArgs e) => e.Modifiers.HasFlag(Application.Instance.AlternateModifier);

    public static T? GetValueOrDefault<T>(this T[] array, int index) where T: class => array.Length > index ? array[index] : null;

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

    public static SKColorF ToSkia(this Color color) => new(color.R, color.G, color.B, color.A);
    public static Color ToEto(this SKColorF color) => new(color.Red, color.Green, color.Blue, color.Alpha);

    private static readonly MethodInfo? m_FixScrollable = Assembly.GetEntryAssembly()?.GetType("CelesteStudio.WPF.Program")?.GetMethod("FixScrollable", BindingFlags.Public | BindingFlags.Static);
    public static Scrollable FixBorder(this Scrollable scrollable) {
        if (!Eto.Platform.Instance.IsWpf) {
            return scrollable;
        }

        // Apply the WPF theme to the border
        m_FixScrollable!.Invoke(null, [scrollable]);
        Settings.ThemeChanged += () => m_FixScrollable.Invoke(null, [scrollable]);
        return scrollable;
    }
}
