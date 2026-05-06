using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eto.Drawing;
using SkiaSharp;
using System.Diagnostics;

namespace CelesteStudio;

public static class FontManager {
    // TODO: Don't hardcode this
    public const float DPI = 96.0f / 72.0f;

    public const string FontFamilyBuiltin = "<builtin>";
    public const string FontFamilyBuiltinDisplayName = "JetBrains Mono (builtin)";

    private static Font? editorFont, statusFont;
    private static SKFont? skEditorFontRegular, skEditorFontBold, skEditorFontItalic, skEditorFontBoldItalic, skStatusFont, skPopupFont, skPopupFontBold;

    public static Font EditorFont => editorFont ??= CreateFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize);
    public static Font StatusFont => statusFont ??= CreateFont(Settings.Instance.FontFamily, Settings.Instance.StatusFontSize);

    public static SKFont SKEditorFontRegular    => skEditorFontRegular    ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom);
    public static SKFont SKEditorFontBold       => skEditorFontBold       ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, FontStyle.Bold);
    public static SKFont SKEditorFontItalic     => skEditorFontItalic     ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, FontStyle.Italic);
    public static SKFont SKEditorFontBoldItalic => skEditorFontBoldItalic ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.EditorFontSize * Settings.Instance.FontZoom, FontStyle.Bold | FontStyle.Italic);
    public static SKFont SKStatusFont           => skStatusFont           ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.StatusFontSize);
    public static SKFont SKPopupFont            => skPopupFont            ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.PopupFontSize);
    public static SKFont SKPopupFontBold        => skPopupFontBold        ??= CreateSKFont(Settings.Instance.FontFamily, Settings.Instance.PopupFontSize, FontStyle.Bold);

    private static FontFamily? builtinFontFamily;
    public static Font CreateFont(string fontFamily, float size, FontStyle style = FontStyle.None) {
        if (fontFamily == FontFamilyBuiltin) {
            var asm = Assembly.GetExecutingAssembly();
            builtinFontFamily ??= FontFamily.FromStreams(asm.GetManifestResourceNames()
                .Where(name => name.StartsWith("JetBrainsMono/"))
                .Select(asm.GetManifestResourceStream));

            return new Font(builtinFontFamily, size, style);
        } else {
            return new Font(fontFamily, size, style);
        }
    }

    public static SKFont CreateSKFont(string fontFamily, float size, FontStyle style = FontStyle.None) {
        if (fontFamily == FontFamilyBuiltin) {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(style switch {
                FontStyle.None => "JetBrainsMono/JetBrainsMono-Regular",
                FontStyle.Bold => "JetBrainsMono/JetBrainsMono-Bold",
                FontStyle.Italic => "JetBrainsMono/JetBrainsMono-Italic",
                FontStyle.Bold | FontStyle.Italic => "JetBrainsMono/JetBrainsMono-BoldItalic",
                _ => throw new UnreachableException(),
            });
            var typeface = SKTypeface.FromStream(stream);

            return new SKFont(typeface, size * DPI) { LinearMetrics = true, Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        } else {
            var typeface = style switch {
                FontStyle.None => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Light, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                FontStyle.Bold => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                FontStyle.Italic => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Light, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic),
                FontStyle.Bold | FontStyle.Italic => SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic),
                _ => throw new UnreachableException(),
            };

            return new SKFont(typeface, size * DPI) { LinearMetrics = true, Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        }
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

    /// Additional spacing in units of line height,
    /// of how much space should be above/below each line
    private const float LineSpacing = 0.025f;

    public static float LineHeight(this SKFont font) {
        return font.Spacing * (1.0f + 2.0f * LineSpacing);
    }
    public static float Offset(this SKFont font) {
        return font.Metrics.Leading - font.Metrics.Ascent + font.Spacing * LineSpacing;
    }

    public static void OnFontChanged() {
        // Clear cached fonts
        editorFont?.Dispose();
        statusFont?.Dispose();

        editorFont = statusFont = null;

        skEditorFontRegular?.Dispose();
        skEditorFontBold?.Dispose();
        skEditorFontItalic?.Dispose();
        skEditorFontBoldItalic?.Dispose();
        skStatusFont?.Dispose();
        skPopupFont?.Dispose();
        skPopupFontBold?.Dispose();
        widthCache.Clear();

        skEditorFontRegular = skEditorFontBold = skEditorFontItalic = skEditorFontBoldItalic = skStatusFont = skPopupFont = skPopupFontBold = null;
    }
}
