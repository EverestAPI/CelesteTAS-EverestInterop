using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using System;
using System.Xml;
using TAS.Module;

namespace TAS.EverestInterop;

/// Provides font drawing with a common baseline across multiple scales
public static class CommonBaselineFont {
    [Load]
    private static void Load() {
        On.Monocle.PixelFont.AddFontSize_string_XmlElement_Atlas_bool += On_AddFontSize;
    }
    [Unload]
    private static void Unload() {
        On.Monocle.PixelFont.AddFontSize_string_XmlElement_Atlas_bool -= On_AddFontSize;
    }

    private const string AttrBaseline = "CelesteTAS_baseline";

    private static PixelFontSize On_AddFontSize(On.Monocle.PixelFont.orig_AddFontSize_string_XmlElement_Atlas_bool orig, Monocle.PixelFont self, string path, XmlElement data, Atlas atlas, bool outline) {
        var pixelFontSize = orig(self, path, data, atlas, outline);

        // Store baseline of font size
        var pixelFontSizeData = DynamicData.For(pixelFontSize);
        pixelFontSizeData.Set(AttrBaseline, data["common"].AttrInt("base"));

        return pixelFontSize;
    }

    /// Draws the specified string while keeping a common baseline at different scales
    public static void DrawCommonBaseline(this PixelFont pixelFont, string text, float baseSize, Vector2 position, Vector2 justify, Vector2 scale, Color color, float edgeDepth, Color edgeColor, float stroke, Color strokeColor) {
        if (string.IsNullOrEmpty(text)) {
            return;
        }

        var pixelFontSize = pixelFont.Get((int)Math.Round(baseSize * Math.Max(scale.X, scale.Y)));
        var pixelFontSizeData = DynamicData.For(pixelFontSize);

        // Rescale based on font pixel size
        scale *= baseSize / pixelFontSize.Size;

        float lineWidth = justify.X != 0.0f ? pixelFontSize.WidthToNextLine(text, 0) : 0.0f;
        int baseline = pixelFontSizeData.Get<int>(AttrBaseline);

        var offset = Vector2.Zero;
        var justified = new Vector2(lineWidth * justify.X, pixelFontSize.HeightOf(text) * justify.Y);

        for (int i = 0; i < text.Length; i++) {
            if (text[i] == '\n') {
                offset.X = 0.0f;
                offset.Y += pixelFontSize.LineHeight;
                if (justify.X != 0.0f) {
                    justified.X = pixelFontSize.WidthToNextLine(text, i + 1) * justify.X;
                }
                continue;
            }

            if (!pixelFontSize.Characters.TryGetValue(text[i], out var c)) {
                continue;
            }

            // Properly align every scale with a common baseline
            var pos = position
                + (offset + justified) * scale
                + new Vector2(c.XOffset * scale.X, c.YOffset - baseline + (baseline - c.YOffset) * (1.0f - scale.Y));

            if (stroke > 0.0f && !pixelFontSize.Outline) {
                if (edgeDepth > 0.0f) {
                    c.Texture.Draw(pos + new Vector2(0.0f, 0.0f - stroke), Vector2.Zero, strokeColor, scale);
                    for (float j = 0f - stroke; j < edgeDepth + stroke; j += stroke) {
                        c.Texture.Draw(pos + new Vector2(0.0f - stroke, j), Vector2.Zero, strokeColor, scale);
                        c.Texture.Draw(pos + new Vector2(stroke, j), Vector2.Zero, strokeColor, scale);
                    }
                    c.Texture.Draw(pos + new Vector2(0.0f - stroke, edgeDepth + stroke), Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2(0.0f, edgeDepth + stroke), Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2(stroke, edgeDepth + stroke), Vector2.Zero, strokeColor, scale);
                } else {
                    c.Texture.Draw(pos + new Vector2(-1.0f, -1.0f) * stroke, Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2( 0.0f, -1.0f) * stroke, Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2( 1.0f, -1.0f) * stroke, Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2(-1.0f,  0.0f) * stroke, Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2( 1.0f,  0.0f) * stroke, Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2(-1.0f,  1.0f) * stroke, Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2( 0.0f,  1.0f) * stroke, Vector2.Zero, strokeColor, scale);
                    c.Texture.Draw(pos + new Vector2( 1.0f,  1.0f) * stroke, Vector2.Zero, strokeColor, scale);
                }
            }

            if (edgeDepth > 0f) {
                c.Texture.Draw(pos + Vector2.UnitY * edgeDepth, Vector2.Zero, edgeColor, scale);
            }

            c.Texture.Draw(pos, Vector2.Zero, color, scale);

            offset.X += c.XAdvance;
            if (i < text.Length - 1 && c.Kerning.TryGetValue(text[i + 1], out int kerning)) {
                offset.X += kerning;
            }
        }
    }
}
