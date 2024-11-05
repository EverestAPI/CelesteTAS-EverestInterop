using System;
using System.Text;
using CelesteStudio.Communication;
using CelesteStudio.Controls;
using CelesteStudio.Data;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using StudioCommunication.Util;
using System.ComponentModel;

namespace CelesteStudio.Editing;

public sealed class GameInfo : Panel {
    private sealed class SubpixelIndicator : SkiaDrawable {
        public override void Draw(SKSurface surface) {
            surface.Canvas.Clear();

            var remainder = CommunicationWrapper.PlayerPositionRemainder;

            float subpixelLeft = (float)Math.Round(remainder.X + 0.5f, CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals, MidpointRounding.AwayFromZero);
            float subpixelTop = (float)Math.Round(remainder.Y + 0.5f, CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals, MidpointRounding.AwayFromZero);
            float subpixelRight = 1.0f - subpixelLeft;
            float subpixelBottom = 1.0f - subpixelTop;

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

            surface.Canvas.DrawText(left, x - rectPadding - font.MeasureWidth(left), y + (rectSize - textHeight) / 2.0f + font.Offset(), font, Settings.Instance.Theme.StatusFgPaint);
            surface.Canvas.DrawText(right, x + rectPadding + rectSize, y + (rectSize - textHeight) / 2.0f + font.Offset(), font, Settings.Instance.Theme.StatusFgPaint);

            surface.Canvas.DrawText(top, MathF.Round(x + (rectSize - font.MeasureWidth(top)) / 2.0f), MathF.Round(y - rectPadding - textHeight + font.Offset()), font, Settings.Instance.Theme.StatusFgPaint);
            surface.Canvas.DrawText(bottom, x + (rectSize - font.MeasureWidth(bottom)) / 2.0f, y + rectPadding + rectSize + font.Offset(), font, Settings.Instance.Theme.StatusFgPaint);

            int boxThickness = Math.Max(1, (int)Math.Round(rectSize / 20.0f));
            float dotThickness = boxThickness * 1.25f;

            using var boxPaint = new SKPaint();
            boxPaint.ColorF = Settings.Instance.Theme.SubpixelIndicatorBox.ToSkia();
            boxPaint.Style = SKPaintStyle.Stroke;
            boxPaint.StrokeWidth = boxThickness;

            surface.Canvas.DrawRect(x, y, rectSize, rectSize, boxPaint);
            surface.Canvas.DrawRect(x + (rectSize - dotThickness) * subpixelLeft, y + (rectSize - dotThickness) * subpixelTop, dotThickness, dotThickness, Settings.Instance.Theme.SubpixelIndicatorDotPaint);

            Width = (int)((textWidth + rectPadding + indicatorPadding) * 2.0f + rectSize);
            Height = (int)((textHeight + rectPadding + indicatorPadding) * 2.0f + rectSize);
        }
    }

    private const string DisconnectedText = "Searching...";

    public bool EditingTemplate => Content == editPanel;

    // The StackLayouts don't fit the exact content size on their own..
    public int ActualWidth => EditingTemplate
        ? (int)(Math.Max(infoTemplateArea.GetPreferredSize().Width, buttonsPanel.GetPreferredSize().Width))
        : (int)(Math.Max(Math.Max(frameInfo.GetPreferredSize().Width, gameStatus.GetPreferredSize().Width), subpixelIndicator.Visible ? subpixelIndicator.GetPreferredSize().Width : 0));
    public int ActualHeight => EditingTemplate
        ? (int)(infoTemplateArea.GetPreferredSize().Height + buttonsPanel.GetPreferredSize().Height)
        : (int)(frameInfo.GetPreferredSize().Height + gameStatus.GetPreferredSize().Height + (subpixelIndicator.Visible ? subpixelIndicator.GetPreferredSize().Height : 0));

    public int AvailableWidth {
        set => infoTemplateArea.Width = Math.Max(0, value);
    }

    private readonly Label frameInfo;
    private readonly Label gameStatus;
    private readonly SubpixelIndicator subpixelIndicator;

    private readonly TextArea infoTemplateArea;
    private readonly Panel buttonsPanel;

    private readonly Panel showPanel;
    private readonly Panel editPanel;

    // Re-use builder to avoid allocations
    private readonly StringBuilder frameInfoBuilder = new();

    public GameInfo() {
        Padding = 0;

        frameInfo = new Label {
            Text = string.Empty,
            TextColor = Settings.Instance.Theme.StatusFg,
            Font = FontManager.StatusFont,
            Wrap = WrapMode.None,
        };
        RecalcFrameInfo();
        gameStatus = new Label {
            Text = string.Empty,
            TextColor = Settings.Instance.Theme.StatusFg,
            Font = FontManager.StatusFont,
            Wrap = WrapMode.None,
        };
        RecalcGameStatus();
        subpixelIndicator = new SubpixelIndicator { Width = 100, Height = 100 };
        subpixelIndicator.Visible = CommunicationWrapper.ShowSubpixelIndicator && Settings.Instance.ShowSubpixelIndicator;
        subpixelIndicator.Invalidate();

        infoTemplateArea = new TextArea {
            Text = string.Empty,
            TextColor = Settings.Instance.Theme.StatusFg,
            Font = FontManager.StatusFont,
            Wrap = false,
        };

        var doneButton = new Button { Text = "Done" };
        var cancelButton = new Button { Text = "Cancel" };
        buttonsPanel = new StackLayout {
            Padding = 5,
            Spacing = 5,
            Orientation = Orientation.Horizontal,
            Items = { doneButton, cancelButton },
        };

        showPanel = new StackLayout { Padding = 0, Items = { frameInfo, gameStatus, subpixelIndicator } };
        editPanel = new StackLayout { Padding = 0, Items = { infoTemplateArea, buttonsPanel } };

        Content = showPanel;

        // Responsive size based on text
        infoTemplateArea.TextChanged += (_, _) => {
            int lineCount = infoTemplateArea.Text.CountLines();
            // Always have an empty line at the bottom
            infoTemplateArea.Height = (int)((lineCount + 1) * infoTemplateArea.Font.LineHeight()) + 2;
        };

        // Finish editing info template
        doneButton.Click += (_, _) => {
            CommunicationWrapper.SetCustomInfoTemplate(infoTemplateArea.Text);
            Content = showPanel;
            OnSizeChanged(EventArgs.Empty);
        };
        cancelButton.Click += (_, _) => {
            Content = showPanel;
            OnSizeChanged(EventArgs.Empty);
        };

        // Update displayed data
        Studio.Instance.Editor.TextChanged += (_, _, _) => {
            RecalcFrameInfo();
        };
        CommunicationWrapper.ConnectionChanged += () => {
            RecalcFrameInfo();
            RecalcGameStatus();
            subpixelIndicator.Visible = CommunicationWrapper.ShowSubpixelIndicator && Settings.Instance.ShowSubpixelIndicator;
            subpixelIndicator.Invalidate();
        };
        CommunicationWrapper.StateUpdated += (_, _) => {
            RecalcFrameInfo();
            RecalcGameStatus();
            subpixelIndicator.Visible = CommunicationWrapper.ShowSubpixelIndicator && Settings.Instance.ShowSubpixelIndicator;
            subpixelIndicator.Invalidate();
        };

        // Manually forward size changes, since the StackLayouts won't do it..
        frameInfo.SizeChanged += ForwardSize;
        gameStatus.SizeChanged += ForwardSize;
        subpixelIndicator.SizeChanged += ForwardSize;

        infoTemplateArea.SizeChanged += ForwardSize;
        buttonsPanel.SizeChanged += ForwardSize;

        void ForwardSize(object? _1, EventArgs _2) {
            Content.Width = ActualWidth;
            Content.Height = ActualHeight;
            OnSizeChanged(EventArgs.Empty);
        }

        // React to settings changes
        Settings.ThemeChanged += () => {
            frameInfo.TextColor = Settings.Instance.Theme.StatusFg;
            gameStatus.TextColor = Settings.Instance.Theme.StatusFg;
            infoTemplateArea.TextColor = Settings.Instance.Theme.StatusFg;
            subpixelIndicator.Invalidate();
        };
        Settings.FontChanged += () => {
            frameInfo.Font = FontManager.StatusFont;
            gameStatus.Font = FontManager.StatusFont;
            infoTemplateArea.Font = FontManager.StatusFont;
            subpixelIndicator.Invalidate();
        };
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

    public void SetupContextMenu(GameInfoPopout? popout = null) {
        var editCustomInfoItem = MenuEntry.Status_EditCustomInfoTemplate.ToAction(() => {
            Content = editPanel;
            OnSizeChanged(EventArgs.Empty);
            infoTemplateArea.Text = CommunicationWrapper.GetCustomInfoTemplate();
        });
        editCustomInfoItem.Enabled = CommunicationWrapper.Connected;
        CommunicationWrapper.ConnectionChanged += () => editCustomInfoItem.Enabled = CommunicationWrapper.Connected;

        ContextMenu = new ContextMenu {
            Items  = {
                MenuEntry.Status_CopyGameInfoToClipboard.ToAction(() => {
                    if (CommunicationWrapper.GetExactGameInfo() is var exactGameInfo && !string.IsNullOrWhiteSpace(exactGameInfo)) {
                        Clipboard.Instance.Clear();
                        Clipboard.Instance.Text = exactGameInfo;
                    }
                }),
                MenuEntry.Status_ReconnectStudioCeleste.ToAction(CommunicationWrapper.ForceReconnect),
                new SeparatorMenuItem(),
                editCustomInfoItem,
                MenuEntry.Status_ClearWatchEntityInfo.ToAction(CommunicationWrapper.ClearWatchEntityInfo),
            }
        };

        if (popout != null) {
            var alwaysOnTopCheckbox = MenuEntry.StatusPopout_AlwaysOnTop.ToCheckbox();
            alwaysOnTopCheckbox.CheckedChanged += (_, _) => {
                popout.Topmost = Settings.Instance.GameInfoPopoutTopmost = alwaysOnTopCheckbox.Checked;
                Settings.Save();
            };
            popout.Topmost = alwaysOnTopCheckbox.Checked = Settings.Instance.GameInfoPopoutTopmost;

            ContextMenu.Items.Add(new SeparatorMenuItem());
            ContextMenu.Items.Add(alwaysOnTopCheckbox);
        }
    }

    private void RecalcFrameInfo() {
        frameInfoBuilder.Clear();

        if (CommunicationWrapper.Connected && CommunicationWrapper.CurrentFrameInTas > 0) {
            frameInfoBuilder.Append(CommunicationWrapper.CurrentFrameInTas);
            frameInfoBuilder.Append('/');
        }
        frameInfoBuilder.Append(Studio.Instance.Editor.TotalFrameCount);

        var document = Studio.Instance.Editor.Document;
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

        frameInfo.Text = frameInfoBuilder.ToString();
    }

    private void RecalcGameStatus() {
        gameStatus.Text = CommunicationWrapper.Connected && CommunicationWrapper.GameInfo is { } gameInfo && !string.IsNullOrEmpty(gameInfo)
            ? gameInfo
            : DisconnectedText;
    }
}

public sealed class GameInfoPanel : Panel {
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

    public GameInfoPanel() {
        var gameInfo = Studio.Instance.GameInfo;
        gameInfo.SetupContextMenu();

        var scrollable = new Scrollable {
            Padding = 0,
            Border = BorderType.None,
            Content = gameInfo,
        };

        var layout = new PixelLayout();
        layout.Add(scrollable, 0, 0);

        GameInfoPopout? popoutForm = null;
        bool forceClosePopout = false;

        Studio.Instance.Exiting += () => {
            forceClosePopout = true;
            popoutForm?.Close();
            forceClosePopout = false;
        };

        var popoutButton = new PopoutButton { Visible = false };
        popoutButton.Click += () => {
            Settings.Instance.GameInfo = GameInfoType.Popout;
            Settings.OnChanged();
            Settings.Save();
        };

        // Only show popout button while hovering Info HUD
        MouseEnter += (_, _) => popoutButton.Visible = !gameInfo.EditingTemplate;
        MouseLeave += (_, _) => popoutButton.Visible = false;

        layout.Add(popoutButton, ClientSize.Width - Padding.Left - Padding.Right - PopoutButton.ButtonSize, 0);

        SizeChanged += (_, _) => {
            if (popoutForm == null) {
                gameInfo.AvailableWidth = ClientSize.Width - Padding.Left - Padding.Right;
            }
            LimitSize();
        };

        Studio.Instance.SizeChanged += (_, _) => LimitSize();
        gameInfo.SizeChanged += (_, _) => LimitSize();

        Padding = 10;
        Content = layout;

        Load += (_, _) => {
            UpdateGameInfoStatus();
        };

        BackgroundColor = Settings.Instance.Theme.StatusBg;
        Settings.Changed += () => {
            UpdateGameInfoStatus();
        };
        Settings.ThemeChanged += () => {
            BackgroundColor = Settings.Instance.Theme.StatusBg;
        };

        return;

        void LimitSize() {
            if (popoutForm != null) {
                return;
            }

            // Causes the game-info to fit to the scrollable again
            scrollable.Content = gameInfo;

            // Limit height to certain percentage of entire the window
            scrollable.Size = new Size(
                Math.Max(0, ClientSize.Width - Padding.Left - Padding.Right),
                Math.Max(0, Math.Min(gameInfo.ActualHeight + Padding.Top + Padding.Bottom, (int)(Studio.Instance.Height * Settings.Instance.MaxGameInfoHeight)) - Padding.Top - Padding.Bottom));

            // Don't show while editing template (cause overlap)
            popoutButton.Visible = !gameInfo.EditingTemplate;

            // Account for scroll bar
            bool scrollBarVisible = gameInfo.Height > scrollable.Height;
            layout.Move(popoutButton, ClientSize.Width - Padding.Left - Padding.Right - PopoutButton.ButtonSize - (scrollBarVisible ? Studio.ScrollBarSize : 0), 0);
        }

        void UpdateGameInfoStatus() {
            switch (Settings.Instance.GameInfo) {
                case GameInfoType.Disabled:
                    forceClosePopout = true;
                    popoutForm?.Close();
                    forceClosePopout = false;

                    Visible = false;
                    OnSizeChanged(EventArgs.Empty); // Changing Visible doesn't send size events
                    break;

                case GameInfoType.Panel:
                    forceClosePopout = true;
                    popoutForm?.Close();
                    forceClosePopout = false;

                    Visible = true;
                    OnSizeChanged(EventArgs.Empty); // Changing Visible doesn't send size events
                    break;

                case GameInfoType.Popout:
                    if (popoutForm != null) {
                        return;
                    }

                    scrollable.Content = null;

                    popoutForm ??= new GameInfoPopout();
                    popoutForm.Closed += (_, _) => {
                        popoutForm.Content = null;
                        popoutForm = null;

                        scrollable.Content = gameInfo;
                        if (!forceClosePopout) {
                            Settings.Instance.GameInfo = GameInfoType.Panel;
                            Settings.OnChanged();
                            Settings.Save();
                        }
                    };
                    popoutForm.Show();

                    Visible = false;
                    OnSizeChanged(EventArgs.Empty); // Changing Visible doesn't send size events
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
public sealed class GameInfoPopout : Form {
    public GameInfoPopout() {
        var gameInfo = Studio.Instance.GameInfo;
        gameInfo.SetupContextMenu(this);

        var scrollable = new Scrollable {
            Padding = 0,
            Border = BorderType.None,
            Content = gameInfo,
        };

        Title = "Game Info";
        MinimumSize = new Size(300, 100);

        Padding = 10;
        Content = scrollable;
        BackgroundColor = Settings.Instance.Theme.StatusBg;
        Settings.ThemeChanged += () => {
            BackgroundColor = Settings.Instance.Theme.StatusBg;
        };

        SizeChanged += (_, _) => {
            gameInfo.AvailableWidth = ClientSize.Width - Padding.Left - Padding.Right;
        };

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

            gameInfo.AvailableWidth = ClientSize.Width - Padding.Left - Padding.Right;
        };
    }

    protected override void OnClosing(CancelEventArgs e) {
        Settings.Instance.GameInfoPopoutLocation = Location;
        Settings.Instance.GameInfoPopoutSize = Size;
        Settings.Save();

        base.OnClosing(e);
    }
}
