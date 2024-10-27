using CelesteStudio.Util;
using Eto.Drawing;
using SkiaSharp;
using System;
using System.Collections.Generic;
using Tomlet.Attributes;

namespace CelesteStudio.Editing;

public record struct Style(Color ForegroundColor, Color? BackgroundColor = null, FontStyle FontStyle = FontStyle.None) {
    public StylePaint CreatePaint() => new(CreateForegroundPaint(), CreateBackgroundPaint(), FontStyle);

    public SKPaint CreateForegroundPaint(SKPaintStyle style = SKPaintStyle.Fill) =>
        new() { ColorF = ForegroundColor.ToSkia(), Style = style, IsAntialias = true };
    public SKPaint? CreateBackgroundPaint(SKPaintStyle style = SKPaintStyle.Fill) =>
        BackgroundColor == null ? null : new() { ColorF = BackgroundColor.Value.ToSkia(), Style = style, IsAntialias = true };
}

public readonly record struct StylePaint(SKPaint ForegroundColor, SKPaint? BackgroundColor = null, FontStyle FontStyle = FontStyle.None) : IDisposable {
    public void Dispose() {
        ForegroundColor.Dispose();
        BackgroundColor?.Dispose();
    }
}

public class Theme {
    // Editor
    public Color Background;
    public Color Caret;
    public Color CurrentLine;
    public Color LineNumber;
    public Color PlayingFrame;
    public Color PlayingLineFg;
    public Color PlayingLineBg;
    public Color Selection;
    public Color SavestateFg;
    public Color SavestateBg;
    public Color ServiceLine;
    public Color StatusFg;
    public Color StatusBg;
    public Color CalculateFg;
    public Color CalculateBg;

    // Status panel
    public Color PopoutButtonBg;
    public Color PopoutButtonHovered;
    public Color PopoutButtonSelected;
    public Color SubpixelIndicatorBox;
    public Color SubpixelIndicatorDot;

    // Popup menu
    public Color PopupMenuFg;
    public Color PopupMenuFgDisabled;
    public Color PopupMenuFgExtra;
    public Color PopupMenuBg;
    public Color PopupMenuSelected;
    public int PopupMenuBorderPadding;
    public float PopupMenuBorderRounding;
    public int PopupMenuEntryHorizontalPadding;
    public int PopupMenuEntryVerticalPadding;
    public int PopupMenuEntrySpacing;
    public float PopupMenuEntryRounding;

    // Inputs
    public Style Action;
    public Style Angle;
    public Style Breakpoint;
    public Style SavestateBreakpoint;
    public Style Delimiter;
    public Style Command;
    public Style Frame;
    public Style Comment;

    #region SKPaint cache
    // Cache SKPaint instances to avoid creating, disposing and configuring them for every draw

    [TomlNonSerialized]
    private StylePaint? _actionPaint, _anglePaint, _breakpointPaint, _savestateBreakpointPaint, _delimiter, _command, _frame, _comment;
    [TomlNonSerialized]
    private SKPaint? _commentBox, _statusFgPaint, _subpixelIndicatorDotPaint, _popupMenuFgPaint, _popupMenuFgDisabledPaint, _popupMenuFgExtraPaint, _popupMenuBgPaint, _popupMenuSelectedPaint;

    [TomlNonSerialized]
    public StylePaint ActionPaint => _actionPaint ??= Action.CreatePaint();
    [TomlNonSerialized]
    public StylePaint AnglePaint => _anglePaint ??= Angle.CreatePaint();
    [TomlNonSerialized]
    public StylePaint BreakpointPaint => _breakpointPaint ??= Breakpoint.CreatePaint();
    [TomlNonSerialized]
    public StylePaint SavestateBreakpointPaint => _savestateBreakpointPaint ??= SavestateBreakpoint.CreatePaint();
    [TomlNonSerialized]
    public StylePaint DelimiterPaint => _delimiter ??= Delimiter.CreatePaint();
    [TomlNonSerialized]
    public StylePaint CommandPaint => _command ??= Command.CreatePaint();
    [TomlNonSerialized]
    public StylePaint FramePaint => _frame ??= Frame.CreatePaint();
    [TomlNonSerialized]
    public StylePaint CommentPaint => _comment ??= Comment.CreatePaint();
    [TomlNonSerialized]
    public SKPaint CommentBoxPaint => _commentBox ??= Comment.CreateForegroundPaint(SKPaintStyle.Stroke);

    [TomlNonSerialized]
    public SKPaint StatusFgPaint => _statusFgPaint ??= new SKPaint { ColorF = StatusFg.ToSkia(), Style = SKPaintStyle.Fill, IsAntialias = true };
    [TomlNonSerialized]
    public SKPaint SubpixelIndicatorDotPaint => _subpixelIndicatorDotPaint ??= new SKPaint { ColorF = SubpixelIndicatorDot.ToSkia(), Style = SKPaintStyle.Fill, IsAntialias = true };
    [TomlNonSerialized]
    public SKPaint PopupMenuFgPaint => _popupMenuFgPaint ??= new SKPaint { ColorF = PopupMenuFg.ToSkia(), Style = SKPaintStyle.Fill, IsAntialias = true };
    [TomlNonSerialized]
    public SKPaint PopupMenuFgDisabledPaint => _popupMenuFgDisabledPaint ??= new SKPaint { ColorF = PopupMenuFgDisabled.ToSkia(), Style = SKPaintStyle.Fill, IsAntialias = true };
    [TomlNonSerialized]
    public SKPaint PopupMenuFgExtraPaint => _popupMenuFgExtraPaint ??= new SKPaint { ColorF = PopupMenuFgExtra.ToSkia(), Style = SKPaintStyle.Fill, IsAntialias = true };
    [TomlNonSerialized]
    public SKPaint PopupMenuBgPaint => _popupMenuBgPaint ??= new SKPaint { ColorF = PopupMenuBg.ToSkia(), Style = SKPaintStyle.Fill, IsAntialias = true };
    [TomlNonSerialized]
    public SKPaint PopupMenuSelectedPaint => _popupMenuSelectedPaint ??= new SKPaint { ColorF = PopupMenuSelected.ToSkia(), Style = SKPaintStyle.Fill, IsAntialias = true };

    public void InvalidateCache() {
        _actionPaint?.Dispose();
        _anglePaint?.Dispose();
        _breakpointPaint?.Dispose();
        _savestateBreakpointPaint?.Dispose();
        _delimiter?.Dispose();
        _command?.Dispose();
        _frame?.Dispose();
        _comment?.Dispose();
        _commentBox?.Dispose();

        _statusFgPaint?.Dispose();
        _subpixelIndicatorDotPaint?.Dispose();
        _popupMenuFgPaint?.Dispose();
        _popupMenuFgDisabledPaint?.Dispose();
        _popupMenuFgExtraPaint?.Dispose();
        _popupMenuBgPaint?.Dispose();
        _popupMenuSelectedPaint?.Dispose();

        _actionPaint = _anglePaint = _breakpointPaint = _savestateBreakpointPaint = _delimiter = _command = _frame = _comment = null;
        _commentBox = _statusFgPaint = _subpixelIndicatorDotPaint = _popupMenuFgPaint = _popupMenuFgDisabledPaint = _popupMenuFgExtraPaint = _popupMenuBgPaint = _popupMenuSelectedPaint = null;
    }

    #endregion

    public bool DarkMode;

    public const string BuiltinLight = "Light";
    public const string BuiltinDark = "Dark";
    public const string BuiltinLegacyLight = "Legacy Light";
    public const string BuiltinLegacyDark = "Legacy Dark";

    public static readonly Dictionary<string, Theme> BuiltinThemes = new() {
        { BuiltinLight, new() {
            Background = Color.FromRgb(0xEBEBEB),
            Caret = Color.FromRgb(0x1B1B1B),
            CurrentLine = Color.FromArgb(0x27, 0x27, 0x27, 0x20),
            LineNumber = Color.FromRgb(0x363636),
            PlayingFrame = Color.FromArgb(0x24, 0x9D, 0x66, 0xE4),
            PlayingLineFg = Color.FromRgb(0x090909),
            PlayingLineBg = Color.FromRgb(0x47CB8E),
            Selection = Color.FromArgb(0x25, 0x63, 0xC0, 0x39),
            SavestateFg = Color.FromRgb(0xFFFFFF),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0xC6C6C6),
            StatusFg = Color.FromRgb(0x0F0F0F),
            StatusBg = Color.FromRgb(0xE1E1E1),
            CalculateFg = Color.FromRgb(0xCBCBCB),
            CalculateBg = Color.FromRgb(0x6C6C6C),

            PopoutButtonBg = Color.FromRgb(0xCFCFCF),
            PopoutButtonHovered = Color.FromRgb(0xC1C1C1),
            PopoutButtonSelected = Color.FromRgb(0xA9A9A9),
            SubpixelIndicatorBox = Color.FromRgb(0x159F15),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            PopupMenuFg = Color.FromRgb(0x121212),
            PopupMenuFgDisabled = Color.FromRgb(0x8a8a8a),
            PopupMenuFgExtra = Color.FromRgb(0x595959),
            PopupMenuBg = Color.FromRgb(0xD3D3D3),
            PopupMenuSelected = Color.FromArgb(0x25, 0x25, 0x25, 0x4A),
            PopupMenuBorderPadding = 5,
            PopupMenuBorderRounding = 6.0f,
            PopupMenuEntryHorizontalPadding = 5,
            PopupMenuEntryVerticalPadding = 5,
            PopupMenuEntrySpacing = 1,
            PopupMenuEntryRounding = 5.0f,

            Action = new Style(Color.FromRgb(0x1F7BEC)),
            Angle = new Style(Color.FromRgb(0xC835C8)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x727272)),
            Command = new Style(Color.FromRgb(0xBC6628)),
            Comment = new Style(Color.FromRgb(0x289628)),
            Frame = new Style(Color.FromRgb(0x9C32B8)),

            DarkMode = false,
        }},
        { BuiltinDark, new() {
            Background = Color.FromRgb(0x202020),
            Caret = Color.FromRgb(0xDFDFDF),
            CurrentLine = Color.FromArgb(0x94, 0x94, 0x94, 0x2C),
            LineNumber = Color.FromRgb(0x8D8D8D),
            PlayingFrame = Color.FromArgb(0xDE, 0xB4, 0x65, 0xE7),
            PlayingLineFg = Color.FromRgb(0xFAFAFA),
            PlayingLineBg = Color.FromRgb(0xDEA73D),
            Selection = Color.FromArgb(0x25, 0x63, 0xC0, 0x47),
            SavestateFg = Color.FromRgb(0xFAFAFA),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0x4E4E4E),
            StatusFg = Color.FromRgb(0xF8F8F8),
            StatusBg = Color.FromRgb(0x303030),
            CalculateFg = Color.FromRgb(0xE8E8E8),
            CalculateBg = Color.FromRgb(0x4682B4),

            PopoutButtonBg = Color.FromRgb(0x3B3B3B),
            PopoutButtonHovered = Color.FromRgb(0x4C4C4C),
            PopoutButtonSelected = Color.FromRgb(0x646464),
            SubpixelIndicatorBox = Color.FromRgb(0x29A229),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            PopupMenuFg = Color.FromRgb(0xDFDFDF),
            PopupMenuFgDisabled = Color.FromRgb(0x909090),
            PopupMenuFgExtra = Color.FromRgb(0x9F9F9F),
            PopupMenuBg = Color.FromRgb(0x2C2C2C),
            PopupMenuSelected = Color.FromArgb(0x30, 0x50, 0x91, 0x96),
            PopupMenuBorderPadding = 5,
            PopupMenuBorderRounding = 6.0f,
            PopupMenuEntryHorizontalPadding = 5,
            PopupMenuEntryVerticalPadding = 5,
            PopupMenuEntrySpacing = 1,
            PopupMenuEntryRounding = 5.0f,

            Action = new Style(Color.FromRgb(0x8BE9FD)),
            Angle = new Style(Color.FromRgb(0xFF79C6)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x707996)),
            Command = new Style(Color.FromRgb(0xFFB86C)),
            Comment = new Style(Color.FromRgb(0xA6D76A)),
            Frame = new Style(Color.FromRgb(0xA485D0)),

            DarkMode = true,
        }},

        { BuiltinLegacyLight, new() {
            Background = Color.FromRgb(0xFFFFFF),
            Caret = Color.FromRgb(0x000000),
            CurrentLine = Color.FromArgb(0x20000000),
            LineNumber = Color.FromRgb(0x000000),
            PlayingFrame = Color.FromRgb(0x22A022),
            PlayingLineFg = Color.FromRgb(0x000000),
            PlayingLineBg = Color.FromRgb(0x55FF55),
            Selection = Color.FromArgb(0x20000000),
            SavestateFg = Color.FromRgb(0xFFFFFF),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0xC0C0C0),
            StatusFg = Color.FromRgb(0x000000),
            StatusBg = Color.FromRgb(0xF2F2F2),
            CalculateFg = Color.FromRgb(0xCBCBCB),
            CalculateBg = Color.FromRgb(0x6C6C6C),

            PopoutButtonBg = Color.FromRgb(0xCFCFCF),
            PopoutButtonHovered = Color.FromRgb(0xC1C1C1),
            PopoutButtonSelected = Color.FromRgb(0xA9A9A9),
            SubpixelIndicatorBox = Color.FromRgb(0x159F15),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            PopupMenuFg = Color.FromRgb(0x121212),
            PopupMenuFgDisabled = Color.FromRgb(0x8a8a8a),
            PopupMenuFgExtra = Color.FromRgb(0x646464),
            PopupMenuBg = Color.FromRgb(0xE9E9E9),
            PopupMenuSelected = Color.FromArgb(0x44, 0x44, 0x44, 0x3F),
            PopupMenuBorderPadding = 1,
            PopupMenuBorderRounding = 0.0f,
            PopupMenuEntryHorizontalPadding = 3,
            PopupMenuEntryVerticalPadding = 3,
            PopupMenuEntrySpacing = 0,
            PopupMenuEntryRounding = 0.0f,

            Action = new Style(Color.FromRgb(0x2222FF)),
            Angle = new Style(Color.FromRgb(0xEE22EE)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x808080)),
            Command = new Style(Color.FromRgb(0xD2691E)),
            Comment = new Style(Color.FromRgb(0x00A000)),
            Frame = new Style(Color.FromRgb(0xFF2222)),

            DarkMode = false,
        } },
        { BuiltinLegacyDark, new() {
            Background = Color.FromRgb(0x282A36),
            Caret = Color.FromRgb(0xAEAFAD),
            CurrentLine = Color.FromArgb(0x29B4B6C7),
            LineNumber = Color.FromRgb(0x6272A4),
            PlayingFrame = Color.FromRgb(0xF1FA8C),
            PlayingLineFg = Color.FromRgb(0x6272A4),
            PlayingLineBg = Color.FromRgb(0xF1FA8C),
            Selection = Color.FromArgb(0x20B4B6C7),
            SavestateFg = Color.FromRgb(0xF8F8F2),
            SavestateBg = Color.FromRgb(0x4682B4),
            ServiceLine = Color.FromRgb(0x44475A),
            StatusFg = Color.FromRgb(0xF8F8F2),
            StatusBg = Color.FromRgb(0x383A46),
            CalculateFg = Color.FromRgb(0xE8E8E8),
            CalculateBg = Color.FromRgb(0x4682B4),

            PopoutButtonBg = Color.FromRgb(0x494C5F),
            PopoutButtonHovered = Color.FromRgb(0x595D74),
            PopoutButtonSelected = Color.FromRgb(0x6F738F),
            SubpixelIndicatorBox = Color.FromRgb(0x1AB353),
            SubpixelIndicatorDot = Color.FromRgb(0xE30E0E),

            PopupMenuFg = Color.FromRgb(0xDFDFDF),
            PopupMenuFgDisabled = Color.FromRgb(0x909090),
            PopupMenuFgExtra = Color.FromRgb(0x9F9F9F),
            PopupMenuBg = Color.FromRgb(0x2F303B),
            PopupMenuSelected = Color.FromArgb(0xBB, 0xBB, 0xC4, 0x4F),
            PopupMenuBorderPadding = 1,
            PopupMenuBorderRounding = 0.0f,
            PopupMenuEntryHorizontalPadding = 3,
            PopupMenuEntryVerticalPadding = 3,
            PopupMenuEntrySpacing = 0,
            PopupMenuEntryRounding = 0.0f,

            Action = new Style(Color.FromRgb(0x8BE9FD)),
            Angle = new Style(Color.FromRgb(0xFF79C6)),
            Breakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0xFF5555), FontStyle.Bold),
            SavestateBreakpoint = new Style(Color.FromRgb(0xFFFFFF), Color.FromRgb(0x4682B4), FontStyle.Bold),
            Delimiter = new Style(Color.FromRgb(0x6272A4)),
            Command = new Style(Color.FromRgb(0xFFB86C)),
            Comment = new Style(Color.FromRgb(0x95B272)),
            Frame = new Style(Color.FromRgb(0xBD93F9)),

            DarkMode = true,
        } },
    };
}
