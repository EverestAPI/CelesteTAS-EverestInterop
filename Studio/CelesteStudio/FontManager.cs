using Eto;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eto.Drawing;
using SkiaSharp;
using System;

namespace CelesteStudio;

public static class FontManager {
    public const string FontFamilyBuiltin = "<builtin>";
#if MACOS
    public const string FontFamilyBuiltinDisplayName = "Monaco (builtin)";
#else
    public const string FontFamilyBuiltinDisplayName = "JetBrains Mono (builtin)";
#endif

    private static Font? editorFontRegular, editorFontBold, editorFontItalic, editorFontBoldItalic, statusFont, popupFont;
    private static SKFont? skEditorFontRegular;

    public static Font EditorFontRegular    => editorFontRegular    ??= CreateEditor(FontStyle.None);
    public static Font EditorFontBold       => editorFontBold       ??= CreateEditor(FontStyle.Bold);
    public static Font EditorFontItalic     => editorFontItalic     ??= CreateEditor(FontStyle.Italic);
    public static Font EditorFontBoldItalic => editorFontBoldItalic ??= CreateEditor(FontStyle.Bold | FontStyle.Italic);
    public static Font StatusFont           => statusFont           ??= CreateStatus();
    public static Font PopupFont            => popupFont            ??= CreatePopup();

    public static SKFont SKEditorFontRegular => skEditorFontRegular ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom);

    private static FontFamily? builtinFontFamily;
    public static Font CreateFont(string fontFamily, float size, FontStyle style = FontStyle.None) {
        if (Platform.Instance.IsMac && fontFamily == FontFamilyBuiltin) {
            // The built-in font is broken on macOS for some reason, so fallback to a system font
            fontFamily = "Monaco";
        }

        if (fontFamily == FontFamilyBuiltin) {
            var asm = Assembly.GetExecutingAssembly();
            builtinFontFamily ??= FontFamily.FromStreams(asm.GetManifestResourceNames()
                .Where(name => name.StartsWith("JetBrainsMono/"))
                .Select(name => asm.GetManifestResourceStream(name)));

            return new Font(builtinFontFamily, size, style);
        } else {
            return new Font(fontFamily, size, style);
        }
    }

    public static SKFont CreateSKFont(string fontFamily, float size) {
        if (Platform.Instance.IsMac && fontFamily == FontFamilyBuiltin) {
            // The built-in font is broken on macOS for some reason, so fallback to a system font
            fontFamily = "Monaco";
        }

        if (fontFamily == FontFamilyBuiltin) {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JetBrainsMono/JetBrainsMono-Regular");
            return new SKFont(SKTypeface.FromStream(stream), size);
        } else {
            return new SKFont(SKTypeface.FromFamilyName(fontFamily), size);
        }
    }

    private static readonly Dictionary<Font, float> charWidthCache = new();
    public static float CharWidth(this Font font) {
        if (charWidthCache.TryGetValue(font, out float width)) {
            return width;
        }

        width = font.MeasureString("X").Width;
        charWidthCache.Add(font, width);
        return width;
    }
    public static float LineHeight(this Font font) {
        if (Eto.Platform.Instance.IsWpf) {
            // WPF reports the line height a bit to small for some reason?
            return font.LineHeight + 5.0f;
        }

        return font.LineHeight;
    }
    public static float MeasureWidth(this Font font, string text, bool measureReal = false) {
        if (measureReal) {
            return string.IsNullOrEmpty(text) ? 0.0f : font.MeasureString(text).Width;
        }

        return font.CharWidth() * text.Length;
    }

    public static void OnFontChanged() {
        // Clear cached fonts
        editorFontRegular?.Dispose();
        editorFontBold?.Dispose();
        editorFontItalic?.Dispose();
        editorFontBoldItalic?.Dispose();
        statusFont?.Dispose();
        popupFont?.Dispose();
        charWidthCache.Clear();

        editorFontRegular = editorFontBold = editorFontItalic = editorFontBoldItalic = statusFont = popupFont = null;

        skEditorFontRegular?.Dispose();

        skEditorFontRegular = null;
    }

    private static Font CreateEditor(FontStyle style) => CreateFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, style);
    private static Font CreateStatus() => CreateFont(Settings.Instance.FontFamily, Settings.Instance.StatusFontSize);
    private static Font CreatePopup() => CreateFont(Settings.Instance.FontFamily, Settings.Instance.PopupFontSize);
}
