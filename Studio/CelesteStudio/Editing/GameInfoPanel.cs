using System;
using System.Text;
using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Editing;

public class GameInfoPanel : Panel {
    private sealed class PopoutButton : Drawable {
        private const int IconSize = 20;
        private const int BackgroundPadding = 5;
        
        public Action? Click;
        public void PerformClick() => Click?.Invoke();
        
        public PopoutButton() {
            Width = Height = IconSize + BackgroundPadding * 2;
        }
        
        protected override void OnPaint(PaintEventArgs e) {
            var mouse = PointFromScreen(Mouse.Position);
            
            // TODO: Theme
            Color bgColor = Color.FromRgb(0x3B3B3B);
            if (mouse.X >= 0.0f && mouse.X <= Width && mouse.Y >= 0.0f && mouse.Y <= Height) {
                if (Mouse.Buttons.HasFlag(MouseButtons.Primary)) {
                    bgColor = Color.FromRgb(0x646464);
                } else {
                    bgColor = Color.FromRgb(0x4C4C4C);
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
    
    private sealed class SubpixelIndicator : Drawable {
        // Cached to avoid spamming GameDataRequest messages
        // TODO: Have the game notify such settings changes
        private readonly int decimals = CommunicationWrapper.GetSubpixelIndicatorDecimals(); 
        
        protected override void OnPaint(PaintEventArgs e) {
            var remainder = CommunicationWrapper.SubpixelRemainder;
            
            float subpixelLeft = (float)Math.Round(remainder.X + 0.5f, decimals, MidpointRounding.AwayFromZero);
            float subpixelTop = (float)Math.Round(remainder.Y + 0.5f, decimals, MidpointRounding.AwayFromZero);
            float subpixelRight = 1.0f - subpixelLeft;
            float subpixelBottom = 1.0f - subpixelTop;

            var font = FontManager.StatusFont;
            
            const float padding = 5.0f;
            float textWidth = font.MeasureWidth("0.".PadRight(decimals + 2, '0'));
            float textHeight = font.LineHeight();

            // TODO: Make this configurable
            float rectSize = textHeight * 3.0f;
            float x = textWidth + padding;
            float y = textHeight + padding;
            
            int hDecimals = Math.Abs(remainder.X) switch {
                0.5f => 0,
                _ => decimals
            };
            int vDecimals = Math.Abs(remainder.Y) switch {
                0.5f => 0,
                _ => decimals
            };
            
            string left = subpixelLeft.ToFormattedString(hDecimals);
            string right = subpixelRight.ToFormattedString(hDecimals);
            string top = subpixelTop.ToFormattedString(vDecimals);
            string bottom = subpixelBottom.ToFormattedString(vDecimals);
            
            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x - padding - font.MeasureWidth(left), y + (rectSize - textHeight) / 2.0f, left);
            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x + padding + rectSize, y + (rectSize - textHeight) / 2.0f, right);
            
            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x + (rectSize - font.MeasureWidth(top)) / 2.0f, y - padding - textHeight, top);
            e.Graphics.DrawText(font, Settings.Instance.Theme.StatusFg, x + (rectSize - font.MeasureWidth(bottom)) / 2.0f, y + padding + rectSize, bottom);
            
            int thickness = Math.Max(1, (int)Math.Round(rectSize / 20.0f));
            using var boxPen = new Pen(Colors.Green, thickness);
            e.Graphics.DrawRectangle(boxPen, x, y, rectSize, rectSize);
            
            e.Graphics.FillRectangle(Colors.Red, x + (rectSize - thickness) * subpixelLeft, y + (rectSize - thickness) * subpixelTop, thickness, thickness);
            
            Width = (int)((textWidth + padding) * 2.0f + rectSize);
            Height = (int)((textHeight + padding) * 2.0f + rectSize);
        }
    }
    
    private sealed class PopoutForm : Form {
        public readonly Label Label;
        public readonly SubpixelIndicator SubpixelIndicator;

        private readonly StackLayout layout;
        
        public PopoutForm(GameInfoPanel gameInfoPanel) {
            Title = "Game Info";
            Icon = Assets.AppIcon;
            
            Label = new Label {
                Text = gameInfoPanel.label.Text,
                TextColor = Settings.Instance.Theme.StatusFg,
                Font = FontManager.StatusFont,
                Wrap = WrapMode.None,
            };
            SubpixelIndicator = new SubpixelIndicator { Width = 100, Height = 100 };
            SubpixelIndicator.Invalidate();

            Content = layout = new StackLayout { 
                Padding = 10,
                Spacing = 10,
                Items = { Label, SubpixelIndicator }
            };
            
            var alwaysOnTopCheckbox = new CheckMenuItem { Text = "Always on Top" };
            alwaysOnTopCheckbox.CheckedChanged += (_, _) => {
                Topmost = Settings.Instance.SubpixelPopoutTopmost = alwaysOnTopCheckbox.Checked;
                Settings.Save();
            };
            alwaysOnTopCheckbox.Checked = Settings.Instance.SubpixelPopoutTopmost;
            ContextMenu = new ContextMenu {
                Items  = { alwaysOnTopCheckbox }
            };
            
            Resizable = false;
            ShowInTaskbar = false;
            
            Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
            if (!Settings.Instance.SubpixelPopoutLocation.IsZero) {
                Shown += (_, _) => Location = Settings.Instance.SubpixelPopoutLocation; 
            }
        }
        
        protected override void OnClosed(EventArgs e) {
            Settings.Instance.SubpixelPopoutLocation = Location;
            Settings.Save();
            
            base.OnClosed(e);
        }
        
        public void FitSize() {
            var labelSize = Label.GetPreferredSize();
            Size = new Size((int)labelSize.Width, (int)labelSize.Height + layout.Spacing + SubpixelIndicator.Height) + layout.Padding.Size + new Size(2, 2);
        }
        
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Buttons.HasFlag(MouseButtons.Alternate)) {
                ContextMenu.Show();
                e.Handled = true;
                return;
            }
            
            base.OnMouseDown(e);
        }
    }
    
    private const string DisconnectedText = "Searching...";
    
    public int TotalFrames;
    
    private readonly Label label;
    private readonly SubpixelIndicator subpixelIndicator;
    
    private PopoutForm? popoutForm;
    
    public GameInfoPanel() {
        label = new Label {
            Text = DisconnectedText,
            TextColor = Settings.Instance.Theme.StatusFg,
            Font = FontManager.StatusFont,
            Wrap = WrapMode.None,
        };
        subpixelIndicator = new SubpixelIndicator { Width = 100, Height = 100 };
        
        BackgroundColor = Settings.Instance.Theme.StatusBg;
        
        Settings.Changed += () => {
            Visible = Settings.Instance.ShowGameInfo && popoutForm == null;
            subpixelIndicator.Invalidate();
            UpdateLayout();
            Studio.Instance.RecalculateLayout();
        };
        Settings.ThemeChanged += () => {
            label.TextColor = Settings.Instance.Theme.StatusFg;
            BackgroundColor = Settings.Instance.Theme.StatusBg;
            subpixelIndicator.Invalidate();
        };
        Settings.FontChanged += () => {
            label.Font = FontManager.StatusFont;
            subpixelIndicator.Invalidate();
            UpdateLayout();
            Studio.Instance.RecalculateLayout();
        };
        
        var layout = new PixelLayout();
        layout.Add(new StackLayout { Spacing = 10, Items = { label, subpixelIndicator }}, 0, 0);
        
        // This needs to be done *before* the popout subscribes to this event 
        Studio.Instance.Closed += (_, _) => {
            Settings.Instance.SubpixelPopoutOpen = popoutForm != null;
            Settings.Save();
        };
        
        const int popoutPaddingX = 10;
        const int popoutPaddingY = 0;
        var popoutButton = new PopoutButton();
        popoutButton.Click += () => {
            popoutForm ??= new(this);
            popoutForm.Closed += (_, _) => {
                label.Text = popoutForm.Label.Text;
                popoutForm = null;

                Visible = Settings.Instance.ShowGameInfo;
                subpixelIndicator.Invalidate();
                UpdateGameInfo();
                UpdateLayout();
                Studio.Instance.RecalculateLayout();
            };
            Studio.Instance.Closed += (_, _) => popoutForm.Close();
            popoutForm.Show();
            
            Visible = false;
            subpixelIndicator.Invalidate();
            UpdateGameInfo();
            UpdateLayout();
            Studio.Instance.RecalculateLayout();
        };
        
        layout.Add(popoutButton, ClientSize.Width - popoutButton.Width - popoutPaddingX, popoutPaddingY);
        label.SizeChanged += (_, _) => {
            UpdateLayout();
            Studio.Instance.RecalculateLayout();
        };
        SizeChanged += (_, _) => layout.Move(popoutButton, ClientSize.Width - popoutButton.Width - popoutPaddingX, popoutPaddingY);
        
        Padding = 5;
        Content = layout;
        ContextMenu = new ContextMenu {
            Items = {
                MenuUtils.CreateAction("Copy Game Info to Clipboard", Application.Instance.CommonModifier | Keys.Shift | Keys.C, () => {
                    if (CommunicationWrapper.GetExactGameInfo() is var exactGameInfo && !string.IsNullOrWhiteSpace(exactGameInfo)) {
                        Clipboard.Instance.Clear();
                        Clipboard.Instance.Text = exactGameInfo;
                    }
                }),
                MenuUtils.CreateAction("Reconnect Studio and Celeste", Application.Instance.CommonModifier | Keys.Shift | Keys.D, CommunicationWrapper.ForceReconnect),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Copy Custom Info Template to Clipboard", Keys.None, CommunicationWrapper.CopyCustomInfoTemplateToClipboard),
                MenuUtils.CreateAction("Set Custom Info Template from Clipboard", Keys.None, CommunicationWrapper.SetCustomInfoTemplateFromClipboard),
                MenuUtils.CreateAction("Clear Custom Info Template", Keys.None, CommunicationWrapper.ClearCustomInfoTemplate),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Clear Watch Entity Info", Keys.None, CommunicationWrapper.ClearWatchEntityInfo),
            }
        };
        
        CommunicationWrapper.StateUpdated += (prevState, state) => {
            if (!Settings.Instance.ShowGameInfo || prevState.GameInfo == state.GameInfo)
                return;
            
            if (prevState.TotalFrames != state.TotalFrames)
                TotalFrames = state.TotalFrames;
            
            UpdateGameInfo();
        };
        CommunicationWrapper.ConnectionChanged += UpdateGameInfo;
        
        if (Settings.Instance.SubpixelPopoutOpen) {
            Load += (_, _) => popoutButton.PerformClick();
        }
        Shown += (_, _) => UpdateGameInfo();
    }
        
    public void UpdateGameInfo() {
        var frameInfo = new StringBuilder();
        if (CommunicationWrapper.CurrentFrameInTas > 0) {
            frameInfo.Append($"{CommunicationWrapper.CurrentFrameInTas}/");
        }
        frameInfo.Append(TotalFrames.ToString());
        
        var document = Application.Instance.Invoke(() => Studio.Instance.Editor.Document);
        if (!document.Selection.Empty) {
            int minRow = document.Selection.Min.Row;
            int maxRow = document.Selection.Max.Row;
            
            int selectedFrames = 0;
            for (int row = minRow; row <= maxRow; row++) {
                if (!ActionLine.TryParse(document.Lines[row], out var actionLine)) {
                    continue;
                }
                selectedFrames += actionLine.Frames;
            }
            
            frameInfo.Append($" Selected: {selectedFrames}");
        }
        
        var newText = $"{frameInfo}{Environment.NewLine}" + (CommunicationWrapper.Connected && CommunicationWrapper.GameInfo is { } gameInfo
            ? gameInfo.Trim()
            : DisconnectedText);
        
        Application.Instance.InvokeAsync(() => {
            if (popoutForm != null) {
                popoutForm.Label.Text = newText;
                popoutForm.SubpixelIndicator.Invalidate();
                popoutForm.FitSize();
            } else {
                label.Text = newText;
                subpixelIndicator.Invalidate();
            }
        });
    }
    
    protected override void OnMouseDown(MouseEventArgs e) {
        if (e.Buttons.HasFlag(MouseButtons.Alternate))
            ContextMenu.Show();
        
        base.OnMouseDown(e);
    }
}