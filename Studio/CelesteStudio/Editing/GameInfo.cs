using CelesteStudio.Communication;
using CelesteStudio.Controls;
using CelesteStudio.Dialog;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace CelesteStudio.Editing;

public class GameInfoPanel : Panel {
    private sealed class PopoutButton : Drawable {
        public const int ButtonSize = IconSize + BackgroundPadding * 2;

        private const int IconSize = 20;
        private const int BackgroundPadding = 5;

        public Action? Click;
        private void PerformClick() => Click?.Invoke();

        public PopoutButton() {
            Width = Height = ButtonSize;
        }

        protected override void OnPaint(PaintEventArgs e) {
            var mouse = PointFromScreen(Mouse.Position);

            Color bgColor = Settings.Instance.Theme.PopoutButtonBg;
            if (mouse.X >= 0.0f && mouse.X <= Width && mouse.Y >= 0.0f && mouse.Y <= Height) {
                if (Mouse.Buttons.HasFlag(MouseButtons.Primary)) {
                    bgColor = Settings.Instance.Theme.PopoutButtonSelected;
                } else {
                    bgColor = Settings.Instance.Theme.PopoutButtonHovered;
                }
            }

            e.Graphics.FillPath(bgColor, GraphicsPath.GetRoundRect(new RectangleF(0.0f, 0.0f, Width, Height), BackgroundPadding * 1.5f));

            e.Graphics.TranslateTransform(BackgroundPadding, BackgroundPadding);
            e.Graphics.ScaleTransform(IconSize);
            e.Graphics.FillPath(Settings.Instance.Theme.StatusFg, Assets.PopoutPath);
        }

        protected override void OnMouseDown(MouseEventArgs e) => Invalidate();
        protected override void OnMouseUp(MouseEventArgs e) {
            PerformClick();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e) => Invalidate();
        protected override void OnMouseEnter(MouseEventArgs e) => Invalidate();
        protected override void OnMouseLeave(MouseEventArgs e) => Invalidate();
    }

    private PixelLayout? layout;
    private readonly GameInfo gameInfo;
    private readonly PopoutButton popoutButton;

    public bool Active {
        set {
            if (value && layout == null) {
                layout = new();
                layout.Add(gameInfo, 0, 0);
                layout.Add(popoutButton, ClientSize.Width - Padding.Left - Padding.Right - PopoutButton.ButtonSize, 0);

                ContextMenu = GameInfo.CreateContextMenu(gameInfo, popout: false);
            } else if (!value && layout != null) {
                layout.Remove(gameInfo);
                layout.Remove(popoutButton);
                layout = null;

                ContextMenu = null;
            }

            Content = value ? layout : null;
        }
        get => Content != null;
    }

    public GameInfoPanel(GameInfo info, bool active) {
        gameInfo = info;

        popoutButton = new PopoutButton { Visible = true };
        popoutButton.Click += () => {
            Settings.Instance.GameInfo = GameInfoType.Popout;
            Settings.OnChanged();
            Settings.Save();
        };

        gameInfo.SizeChanged += (_, _) => {
            bool vScrollBarVisible;
            if (Eto.Platform.Instance.IsWpf) {
                vScrollBarVisible = gameInfo.ScrollSize.Height > gameInfo.ClientSize.Height;
            } else {
                const int border = 1;
                vScrollBarVisible = gameInfo.ScrollSize.Height > gameInfo.ClientSize.Height + border;
            }
            layout?.Move(popoutButton, gameInfo.Width - gameInfo.Padding.Left - gameInfo.Padding.Right - PopoutButton.ButtonSize - (vScrollBarVisible ? Studio.ScrollBarSize : 0), gameInfo.Padding.Top);
        };

        // Only show popout button while hovering Info HUD
        Shown += (_, _) => popoutButton.Visible = PointFromScreen(Mouse.Position) is var mousePos &&
                                                  mousePos.X >= 0.0f && mousePos.Y >= 0.0f &&
                                                  mousePos.X < ClientSize.Width && mousePos.Y < ClientSize.Height;
        MouseEnter += (_, _) => popoutButton.Visible = true;
        MouseLeave += (_, _) => popoutButton.Visible = false;

        Active = active;
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        // Context menu doesn't open on its own for some reason
        if (e.Buttons.HasFlag(MouseButtons.Alternate)) {
            ContextMenu?.Show();
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }
}
public class GameInfoPopout : FloatingForm {
    /// Indicates that closing the window should not switch to the panel version
    public bool ForceClose = false;

    public GameInfoPopout(GameInfo info) {
        Topmost = Settings.Instance.GameInfoPopoutTopmost;
        Settings.Changed += () => {
            Topmost = Settings.Instance.GameInfoPopoutTopmost;
        };

        Title = "Game Info";
        MinimumSize = new Size(300, 100);

        Content = info;
        ContextMenu = GameInfo.CreateContextMenu(info, popout: true);

        Studio.RegisterWindow(this, centerWindow: false);
        Shown += (_, _) => {
            Size = Settings.Instance.GameInfoPopoutSize;
            if (!Settings.Instance.GameInfoPopoutLocation.IsZero) {
                var lastLocation = Settings.Instance.GameInfoPopoutLocation;
                var lastSize = Settings.Instance.GameInfoPopoutSize;

                // Clamp to screen
                var screen = Screen.FromRectangle(new RectangleF(lastLocation, lastSize));
                if (lastLocation.X < screen.WorkingArea.Left) {
                    lastLocation = lastLocation with { X = (int)screen.WorkingArea.Left };
                } else if (lastLocation.X + lastSize.Width > screen.WorkingArea.Right) {
                    lastLocation = lastLocation with { X = (int)screen.WorkingArea.Right - lastSize.Width };
                }
                if (lastLocation.Y < screen.WorkingArea.Top) {
                    lastLocation = lastLocation with { Y = (int)screen.WorkingArea.Top };
                } else if (lastLocation.Y + lastSize.Height > screen.WorkingArea.Bottom) {
                    lastLocation = lastLocation with { Y = (int)screen.WorkingArea.Bottom - lastSize.Height };
                }
                Location = lastLocation;
            }
        };
    }

    protected override void OnClosing(CancelEventArgs e) {
        Settings.Instance.GameInfoPopoutLocation = Location;
        Settings.Instance.GameInfoPopoutSize = Size;

        if (!ForceClose) {
            Settings.Instance.GameInfo = GameInfoType.Panel;
            Settings.OnChanged();
        }

        Settings.Save();

        base.OnClosing(e);
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        // Context menu doesn't open on its own for some reason
        if (e.Buttons.HasFlag(MouseButtons.Alternate)) {
            ContextMenu.Show();
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }
}

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

    private static readonly ActionBinding EditCustomInfoTemplate = new("Status_EditCustomInfoTemplate", "Edit Custom Info Template", Binding.Category.Status, Hotkey.None, () => new InfoTemplateForm().Show());
    private static readonly ActionBinding ClearWatchEntityInfo = new("Status_ClearWatchEntityInfo", "Clear Watch-Entity Info", Binding.Category.Status, Hotkey.None, CommunicationWrapper.ClearWatchEntityInfo);

    private static readonly BoolBinding PopoutAlwaysOnTop = new("StatusPopout_AlwaysOnTop", "Always on Top", Binding.Category.StatusPopout, Hotkey.None,
        () => Settings.Instance.GameInfoPopoutTopmost,
        value => {
            Settings.Instance.GameInfoPopoutTopmost = value;
            Settings.OnChanged();
            Settings.Save();
        });

    public static readonly Binding[] AllBindings = [CopyGameInfoToClipboard, ReconnectStudioAndCeleste, EditCustomInfoTemplate, ClearWatchEntityInfo, PopoutAlwaysOnTop];

    #endregion

    public static ContextMenu CreateContextMenu(GameInfo info, bool popout) {
        var menu = new ContextMenu();
        menu.Items.AddRange(CreateItems(popout));

        // Also insert into text menu
        info.infoText.ContextMenu = info.infoText.CreateContextMenu();
        info.infoText.ContextMenu.Items.AddRange(CreateItems(popout).Apply(item => item.Order = -1));
        info.infoText.ContextMenu.Items.AddSeparator(order: -1);

        return menu;

        static IEnumerable<MenuItem> CreateItems(bool popout) {
            var editCustomInfoItem = EditCustomInfoTemplate.CreateItem();
            editCustomInfoItem.Enabled = CommunicationWrapper.Connected;
            CommunicationWrapper.ConnectionChanged += () => editCustomInfoItem.Enabled = CommunicationWrapper.Connected;

            yield return CopyGameInfoToClipboard;
            yield return ReconnectStudioAndCeleste;
            yield return new SeparatorMenuItem();
            yield return editCustomInfoItem;
            yield return ClearWatchEntityInfo;

            if (popout) {
                yield return new SeparatorMenuItem();
                yield return PopoutAlwaysOnTop;
            }
        }
    }

    /// Invoked when the preferred size of the game info is changed
    public event Action? PreferredSizeChanged;

    private const string DisconnectedText = "Searching for Celeste Instance...";
    private static IEnumerable<string> GameInfoLines => (CommunicationWrapper.GameInfo is { } gameInfo && !string.IsNullOrEmpty(gameInfo) ? gameInfo : DisconnectedText).SplitLines();

    private readonly InfoText infoText;

    // Re-use builder to avoid allocations
    private readonly StringBuilder frameInfoBuilder = new();
    /// Current total frame count (including commands if connected to Celeste)
    private int totalFrameCount = 0;

    public GameInfo(Editor editor) {
        infoText = new InfoText(Document.Create(GameInfoLines.Prepend(string.Empty)), this) { ShowLineNumbers = false, PaddingRight = 0.0f, PaddingBottom = 0.0f };

        var subpixelIndicator = new SubpixelIndicator { Width = 100, Height = 100 };
        subpixelIndicator.Visible = CommunicationWrapper.ShowSubpixelIndicator && Settings.Instance.ShowSubpixelIndicator;
        subpixelIndicator.Invalidate();

        this.FixBorder();
        Padding = new Padding(5, 10);
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
