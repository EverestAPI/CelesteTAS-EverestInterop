using Eto;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eto.Drawing;
using SkiaSharp;
using System.Diagnostics;

namespace CelesteStudio;

public static class FontManager {
    public const string FontFamilyBuiltin = "<builtin>";
    public const string FontFamilyBuiltinDisplayName = "JetBrains Mono (builtin)";

    private static Font? editorFont, statusFont;
    private static SKFont? skEditorFontRegular, skEditorFontBold, skEditorFontItalic, skEditorFontBoldItalic, skStatusFont, skPopupFont;

    public static Font EditorFont => editorFont ??= CreateFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize);
    public static Font StatusFont => statusFont ??= CreateFont(Settings.Instance.FontFamily, Settings.Instance.StatusFontSize);

    public static SKFont SKEditorFontRegular    => skEditorFontRegular    ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom);
    public static SKFont SKEditorFontBold       => skEditorFontBold       ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, FontStyle.Bold);
    public static SKFont SKEditorFontItalic     => skEditorFontItalic     ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, FontStyle.Italic);
    public static SKFont SKEditorFontBoldItalic => skEditorFontBoldItalic ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, FontStyle.Bold | FontStyle.Italic);
    public static SKFont SKStatusFont           => skStatusFont           ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.StatusFontSize);
    public static SKFont SKPopupFont            => skPopupFont            ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.PopupFontSize);

    private static FontFamily? builtinFontFamily;
    public static Font CreateFont(string fontFamily, float size, FontStyle style = FontStyle.None) {
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

    public static SKFont CreateSKFont(string fontFamily, float size, FontStyle style = FontStyle.None) {
        // TODO: Don't hardcode this
        const float dpi = 96.0f / 72.0f;

        if (fontFamily == FontFamilyBuiltin) {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(style switch {
                FontStyle.None => "JetBrainsMono/JetBrainsMono-Regular",
                FontStyle.Bold => "JetBrainsMono/JetBrainsMono-Bold",
                FontStyle.Italic => "JetBrainsMono/JetBrainsMono-Italic",
                FontStyle.Bold | FontStyle.Italic => "JetBrainsMono/JetBrainsMono-BoldItalic",
                _ => throw new UnreachableException(),
            });
            var typeface = SKTypeface.FromStream(stream);

            return new SKFont(typeface, size * dpi) { LinearMetrics = true, Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        } else {
            var typeface = style switch {
                FontStyle.None => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Light, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                FontStyle.Bold => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                FontStyle.Italic => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Light, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic),
                FontStyle.Bold | FontStyle.Italic => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic),
                _ => throw new UnreachableException(),
            };

            return new SKFont(typeface, size * dpi) { LinearMetrics = true, Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
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
        if (Platform.Instance.IsWpf) {
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

    private static readonly Dictionary<SKFont, float> widthCache = [];
    public static float CharWidth(this SKFont font) {
        if (widthCache.TryGetValue(font, out float width)) {
            return width;
        }

        font.MeasureText([font.GetGlyph('X')]);
        widthCache[font] = width = font.MeasureText([font.GetGlyph('X')]);
        return width;
    }
    public static float MeasureWidth(this SKFont font, string text) {
        return font.CharWidth() * text.Length;
    }
    // Apply +/- 1.0f for better visuals
    public static float LineHeight(this SKFont font) {
        return font.Spacing + 0.6f;
    }
    public static float Offset(this SKFont font) {
        return -font.Metrics.Ascent + 0.7f;
    }

    public static void OnFontChanged() {
        // Clear cached fonts
        editorFont?.Dispose();
        statusFont?.Dispose();
        charWidthCache.Clear();

        editorFont = statusFont = null;

        skEditorFontRegular?.Dispose();
        skEditorFontBold?.Dispose();
        skEditorFontItalic?.Dispose();
        skEditorFontBoldItalic?.Dispose();
        skStatusFont?.Dispose();
        skPopupFont?.Dispose();
        widthCache.Clear();

        skEditorFontRegular = skEditorFontBold = skEditorFontItalic = skEditorFontBoldItalic = skStatusFont = skPopupFont = null;
    }

    private static Font CreateEditor(FontStyle style) => CreateFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, style);
    private static Font CreateStatus() => CreateFont(Settings.Instance.FontFamily, Settings.Instance.StatusFontSize);
    private static Font CreatePopup() => CreateFont(Settings.Instance.FontFamily, Settings.Instance.PopupFontSize);
}
