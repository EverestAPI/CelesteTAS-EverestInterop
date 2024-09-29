using System;
using System.Text;
using CelesteStudio.Communication;
using CelesteStudio.Data;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication.Util;

namespace CelesteStudio.Editing;

public sealed class GameInfo : Panel {
    private sealed class SubpixelIndicator : Drawable {
        protected override void OnPaint(PaintEventArgs e) {
            var remainder = CommunicationWrapper.SubpixelRemainder;

            float subpixelLeft = (float)Math.Round(remainder.X + 0.5f, CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals, MidpointRounding.AwayFromZero);
            float subpixelTop = (float)Math.Round(remainder.Y + 0.5f, CommunicationWrapper.GameSettings.SubpixelIndicatorDecimals, MidpointRounding.AwayFromZero);
            float subpixelRight = 1.0f - subpixelLeft;
            float subpixelBottom = 1.0f - subpixelTop;

            var font = FontManager.StatusFont;

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

            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x - rectPadding - font.MeasureWidth(left), y + (rectSize - textHeight) / 2.0f, left);
            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x + rectPadding + rectSize, y + (rectSize - textHeight) / 2.0f, right);

            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x + (rectSize - font.MeasureWidth(top)) / 2.0f, y - rectPadding - textHeight, top);
            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x + (rectSize - font.MeasureWidth(bottom)) / 2.0f, y + rectPadding + rectSize, bottom);

            int boxThickness = Math.Max(1, (int)Math.Round(rectSize / 20.0f));
            float dotThickness = boxThickness * 1.25f;
            using var boxPen = new Pen(Settings.Instance.Theme.SubpixelIndicatorBox, boxThickness);

            e.Graphics.DrawRectangle(boxPen, x, y, rectSize, rectSize);
            e.Graphics.FillRectangle(Settings.Instance.Theme.SubpixelIndicatorDot, x + (rectSize - dotThickness) * subpixelLeft, y + (rectSize - dotThickness) * subpixelTop, dotThickness, dotThickness);

            Width = (int)((textWidth + rectPadding + indicatorPadding) * 2.0f + rectSize);
            Height = (int)((textHeight + rectPadding + indicatorPadding) * 2.0f + rectSize);
        }
    }

    private const string DisconnectedText = "Searching...";

    public bool EditingTemplate => Content == editPanel;

    private readonly Label frameInfo;
    private readonly Label gameStatus;
    private readonly SubpixelIndicator subpixelIndicator;

    private readonly TextArea infoTemplateArea;

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
        var buttonsPanel = new StackLayout {
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
        SizeChanged += (_, _) => {
            infoTemplateArea.Width = Math.Max(0, Width);
        };

        // Finish editing info template
        doneButton.Click += (_, _) => {
            CommunicationWrapper.SetCustomInfoTemplate(infoTemplateArea.Text);
            Content = showPanel;
        };
        cancelButton.Click += (_, _) => {
            Content = showPanel;
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
        private const int IconSize = 20;
        private const int BackgroundPadding = 5;

        public Action? Click;
        private void PerformClick() => Click?.Invoke();

        public PopoutButton() {
            Width = Height = IconSize + BackgroundPadding * 2;
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

        var popoutButton = new PopoutButton { Visible = false };
        popoutButton.Click += () => {
            scrollable.Content = null;

            popoutForm ??= new GameInfoPopout();
            popoutForm.Closed += (_, _) => {
                popoutForm.Content = null;
                popoutForm = null;

                scrollable.Content = gameInfo;
                Visible = Settings.Instance.ShowGameInfo;
                OnSizeChanged(EventArgs.Empty); // Changing Visible doesn't send size events
            };
            popoutForm.Show();

            Visible = false;
            OnSizeChanged(EventArgs.Empty); // Changing Visible doesn't send size events
        };

        // Only show popout button while hovering Info HUD
        MouseEnter += (_, _) => popoutButton.Visible = true;
        MouseLeave += (_, _) => popoutButton.Visible = false;

        layout.Add(popoutButton, ClientSize.Width - Padding.Left - Padding.Right - popoutButton.Width, 0);
        SizeChanged += (_, _) => {
            if (popoutForm == null && gameInfo.EditingTemplate) {
                gameInfo.Width = ClientSize.Width - Padding.Left - Padding.Right;
            }
            LimitSize();
        };

        Studio.Instance.SizeChanged += (_, _) => LimitSize();
        gameInfo.SizeChanged += (_, _) => LimitSize();

        Padding = 10;
        Content = layout;

        Visible = Settings.Instance.ShowGameInfo;
        BackgroundColor = Settings.Instance.Theme.StatusBg;
        Settings.Changed += () => {
            Visible = Settings.Instance.ShowGameInfo;
        };
        Settings.ThemeChanged += () => {
            BackgroundColor = Settings.Instance.Theme.StatusBg;
        };

        return;

        void LimitSize() {
            // Limit height to half the window
            scrollable.Size = new Size(
                ClientSize.Width - Padding.Left - Padding.Right,
                Math.Min(gameInfo.Height + Padding.Top + Padding.Bottom, (int)(Studio.Instance.Height * Settings.Instance.MaxGameInfoHeight)) - Padding.Top - Padding.Bottom);

            // Account for scroll bar
            bool scrollBarVisible = gameInfo.Height > scrollable.Height;
            layout.Move(popoutButton, ClientSize.Width - Padding.Left - Padding.Right - popoutButton.Width - (scrollBarVisible ? Studio.ScrollBarSize : 0), 0);
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
        Icon = Assets.AppIcon;
        MinimumSize = new Size(300, 100);

        Padding = 10;
        Content = scrollable;
        BackgroundColor = Settings.Instance.Theme.StatusBg;
        Settings.ThemeChanged += () => {
            BackgroundColor = Settings.Instance.Theme.StatusBg;
        };

        SizeChanged += (_, _) => {
            if (gameInfo.EditingTemplate) {
                gameInfo.Width = ClientSize.Width - Padding.Left - Padding.Right;
            }
            scrollable.Size = new Size(
                ClientSize.Width - Padding.Left - Padding.Right,
                ClientSize.Height - Padding.Top - Padding.Bottom);
        };

        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
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
}
