using CelesteStudio.Communication;
using CelesteStudio.Controls;
using CelesteStudio.Util;
using Eto.Forms;
using SkiaSharp;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CelesteStudio.Editing;

public class GameInfo : Scrollable {
    private sealed class InfoText(Document document, Scrollable scrollable) : TextViewer(document, scrollable) {
        protected override SKFont Font => FontManager.SKStatusFont;

        protected override void SetupBackgroundColor() {
            BackgroundColor = Settings.Instance.Theme.StatusBg;
            Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.StatusBg;
        }

        protected override void DrawCurrentLineHighlight(SKCanvas canvas, float carY, SKPaint fillPaint) {
            // Dont highlight
        }
    }

    private sealed class SubpixelIndicator : SkiaDrawable {
        public override void Draw(SKSurface surface) {
            var canvas = surface.Canvas;

            var remainder = CommunicationWrapper.PlayerPositionRemainder;

            double subpixelLeft = Math.Round(remainder.X + 0.5, CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals, MidpointRounding.AwayFromZero);
            double subpixelTop = Math.Round(remainder.Y + 0.5, CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals, MidpointRounding.AwayFromZero);
            double subpixelRight = 1.0 - subpixelLeft;
            double subpixelBottom = 1.0 - subpixelTop;

            var font = FontManager.SKStatusFont;

            const float indicatorPadding = 8.0f;
            const float rectPadding = 5.0f;
            float textWidth = font.MeasureWidth("0.".PadRight(CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals + 2, '0'));
            float textHeight = font.LineHeight();

            float rectSize = textHeight * Settings.Instance.SubpixelIndicatorScale;
            float x = textWidth + rectPadding + indicatorPadding;
            float y = textHeight + rectPadding + indicatorPadding;

            int hDecimals = Math.Abs(remainder.X) switch {
                0.5f => 0,
                _ => CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals
            };
            int vDecimals = Math.Abs(remainder.Y) switch {
                0.5f => 0,
                _ => CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals
            };

            string left = subpixelLeft.ToFormattedString(hDecimals);
            string right = subpixelRight.ToFormattedString(hDecimals);
            string top = subpixelTop.ToFormattedString(vDecimals);
            string bottom = subpixelBottom.ToFormattedString(vDecimals);

            canvas.DrawText(left, x - rectPadding - font.MeasureWidth(left), y + (rectSize - textHeight) / 2.0f + font.Offset(), font, Settings.Instance.Theme.StatusFgPaint);
            canvas.DrawText(right, x + rectPadding + rectSize, y + (rectSize - textHeight) / 2.0f + font.Offset(), font, Settings.Instance.Theme.StatusFgPaint);

            canvas.DrawText(top, MathF.Round(x + (rectSize - font.MeasureWidth(top)) / 2.0f), MathF.Round(y - rectPadding - textHeight + font.Offset()), font, Settings.Instance.Theme.StatusFgPaint);
            canvas.DrawText(bottom, x + (rectSize - font.MeasureWidth(bottom)) / 2.0f, y + rectPadding + rectSize + font.Offset(), font, Settings.Instance.Theme.StatusFgPaint);

            int boxThickness = Math.Max(1, (int)Math.Round(rectSize / 20.0f));
            float dotThickness = boxThickness * 1.25f;

            using var boxPaint = new SKPaint();
            boxPaint.ColorF = Settings.Instance.Theme.SubpixelIndicatorBox.ToSkia();
            boxPaint.Style = SKPaintStyle.Stroke;
            boxPaint.StrokeWidth = boxThickness;

            canvas.DrawRect(x, y, rectSize, rectSize, boxPaint);
            canvas.DrawRect((float)(x + (rectSize - dotThickness) * subpixelLeft), (float)(y + (rectSize - dotThickness) * subpixelTop), dotThickness, dotThickness, Settings.Instance.Theme.SubpixelIndicatorDotPaint);

            Width = (int)((textWidth + rectPadding + indicatorPadding) * 2.0f + rectSize);
            Height = (int)((textHeight + rectPadding + indicatorPadding) * 2.0f + rectSize);
        }
    }

    #region Bindings

    private static readonly ActionBinding CopyGameInfoToClipboard = new("Status_CopyGameInfoToClipboard", "Copy Game Info to Clipboard", Binding.Category.Status, Hotkey.KeyCtrl(Keys.C | Keys.Shift), () => {
        if (CommunicationWrapper.GetExactGameInfo() is var exactGameInfo && !string.IsNullOrWhiteSpace(exactGameInfo)) {
            Clipboard.Instance.Clear();
            Clipboard.Instance.Text = exactGameInfo;
        }
    });
    private static readonly ActionBinding ReconnectStudioAndCeleste = new("Status_ReconnectStudioCeleste", "Force-reconnect Celeste and Studio", Binding.Category.Status, Hotkey.KeyCtrl(Keys.D | Keys.Shift), CommunicationWrapper.ForceReconnect);

    // TODO:
    private static readonly ActionBinding EditCustomInfoTemplate = new("Status_EditCustomInfoTemplate", "Edit Custom Info Template", Binding.Category.Status, Hotkey.None, () => {}/*Studio.Instance.GameInfo.OnEditCustomInfoTemplate()*/);
    private static readonly ActionBinding ClearWatchEntityInfo = new("Status_ClearWatchEntityInfo", "Clear Watch-Entity Info", Binding.Category.Status, Hotkey.None, CommunicationWrapper.ClearWatchEntityInfo);

    private static readonly BoolBinding PopoutAlwaysOnTop = new("StatusPopout_AlwaysOnTop", "Always on Top", Binding.Category.StatusPopout, Hotkey.None,
        () => Settings.Instance.GameInfoPopoutTopmost,
        value => {
            Settings.Instance.GameInfoPopoutTopmost = value;
            Settings.Save();
        });

    public static readonly Binding[] AllBindings = [CopyGameInfoToClipboard, ReconnectStudioAndCeleste, EditCustomInfoTemplate, ClearWatchEntityInfo, PopoutAlwaysOnTop];

    #endregion

    /// Invoked when the preferred size of the game info is changed
    public event Action? PreferredSizeChanged;

    private const string DisconnectedText = "Searching for Celeste Instance...";
    private static IEnumerable<string> GameInfoLines => (CommunicationWrapper.GameInfo is { } gameInfo && !string.IsNullOrEmpty(gameInfo) ? gameInfo : DisconnectedText).SplitLines();

    // Re-use builder to avoid allocations
    private readonly StringBuilder frameInfoBuilder = new();
    /// Current total frame count (including commands if connected to Celeste)
    private int totalFrameCount = 0;

    public GameInfo(Editor editor) {
        var infoText = new InfoText(Document.Create(GameInfoLines.Prepend(string.Empty)), this) { ShowLineNumbers = false, PaddingRight = 0.0f, PaddingBottom = 0.0f };

        var subpixelIndicator = new SubpixelIndicator { Width = 100, Height = 100 };
        subpixelIndicator.Visible = CommunicationWrapper.ShowSubpixelIndicator && Settings.Instance.ShowSubpixelIndicator;
        subpixelIndicator.Invalidate();

        this.FixBorder();
        Padding = 5;
        Content = new StackLayout {
            Items = { infoText, subpixelIndicator }
        };

        BackgroundColor = Settings.Instance.Theme.StatusBg;
        Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.StatusBg;

        CommunicationWrapper.ConnectionChanged += () => {
            totalFrameCount = CommunicationWrapper.TotalFrames;
            RecalcFrameInfo();

            using (infoText.Document.Update()) {
                using var patch = new Document.Patch(infoText.Document);

                patch.DeleteRange(0, infoText.Document.Lines.Count - 1);
                patch.Insert(0, frameInfoBuilder.ToString());
                patch.InsertRange(1, GameInfoLines);
            }

            subpixelIndicator.Visible = CommunicationWrapper.ShowSubpixelIndicator && Settings.Instance.ShowSubpixelIndicator;
            subpixelIndicator.Invalidate();
        };
        CommunicationWrapper.StateUpdated += (_, newState) => {
            if (!newState.FileNeedsReload) {
                totalFrameCount = newState.TotalFrames;
            }
            RecalcFrameInfo();

            using (infoText.Document.Update()) {
                using var patch = new Document.Patch(infoText.Document);

                patch.DeleteRange(0, infoText.Document.Lines.Count - 1);
                patch.Insert(0, frameInfoBuilder.ToString());
                patch.InsertRange(1, newState.GameInfo.SplitLines());
            }

            subpixelIndicator.Visible = CommunicationWrapper.ShowSubpixelIndicator && Settings.Instance.ShowSubpixelIndicator;
            subpixelIndicator.Invalidate();
        };

        // Update displayed data
        editor.TextChanged += (_, insertions, deletions) => {
            // Adjust total frame count
            foreach (string deletion in deletions.Values) {
                if (!ActionLine.TryParse(deletion, out var actionLine)) {
                    continue;
                }
                totalFrameCount -= actionLine.FrameCount;
            }
            foreach (string insertion in insertions.Values) {
                if (!ActionLine.TryParse(insertion, out var actionLine)) {
                    continue;
                }
                totalFrameCount += actionLine.FrameCount;
            }

            RecalcFrameInfo();
            using (infoText.Document.Update()) {
                using var patch = new Document.Patch(infoText.Document);

                patch.Modify(0, frameInfoBuilder.ToString());
            }
        };
        editor.PostDocumentChanged += newDocument => {
            // Calculate total frame count
            totalFrameCount = 0;
            foreach (string line in newDocument.Lines) {
                if (!ActionLine.TryParse(line, out var actionLine)) {
                    continue;
                }
                totalFrameCount += actionLine.FrameCount;
            }

            RecalcFrameInfo();
            using (infoText.Document.Update()) {
                using var patch = new Document.Patch(infoText.Document);

                patch.Modify(0, frameInfoBuilder.ToString());
            }
        };

        infoText.PreferredSizeChanged += _ => PreferredSizeChanged?.Invoke();
    }

    private void RecalcFrameInfo() {
        var document = Studio.Instance.Editor.Document;
        if (document.UpdateInProgress) {
            return;
        }

        frameInfoBuilder.Clear();

        if (CommunicationWrapper.Connected && CommunicationWrapper.CurrentFrameInTas > 0) {
            frameInfoBuilder.Append(CommunicationWrapper.CurrentFrameInTas);
            frameInfoBuilder.Append('/');
        }
        frameInfoBuilder.Append(totalFrameCount);

        if (!document.Selection.Empty) {
            int minRow = document.Selection.Min.Row;
            int maxRow = document.Selection.Max.Row;

            int selectedFrames = 0;
            for (int row = minRow; row <= maxRow; row++) {
                if (!ActionLine.TryParse(document.Lines[row], out var actionLine)) {
                    continue;
                }
                selectedFrames += actionLine.FrameCount;
            }

            frameInfoBuilder.Append(" Selected: ");
            frameInfoBuilder.Append(selectedFrames);
        }
    }
}
