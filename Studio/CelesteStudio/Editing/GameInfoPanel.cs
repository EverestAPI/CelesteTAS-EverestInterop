using System;
using System.Linq;
using System.Text;
using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Editing;

public class GameInfoPanel : Panel {
    private sealed class PopoutButton : Drawable {
        private const int IconSize = 20;
        private const int BackgroundPadding = 5;
        
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
        protected override void OnMouseUp(MouseEventArgs e) => Invalidate();
        protected override void OnMouseMove(MouseEventArgs e) => Invalidate();
        protected override void OnMouseEnter(MouseEventArgs e) => Invalidate();
        protected override void OnMouseLeave(MouseEventArgs e) => Invalidate();
    }
    
    private const string DisconnectedText = "Searching...";
    
    public int TotalFrames;
    private readonly Label label;
    
    public GameInfoPanel() {
        label = new Label {
            Text = DisconnectedText,
            TextColor = Settings.Instance.Theme.StatusFg,
            Font = FontManager.StatusFont,
        };
        
        BackgroundColor = Settings.Instance.Theme.StatusBg;
        
        Settings.Changed += () => {
            Visible = Settings.Instance.ShowGameInfo;
            UpdateLayout();
            Studio.Instance.RecalculateLayout();
        };
        Settings.ThemeChanged += () => {
            label.TextColor = Settings.Instance.Theme.StatusFg;
            BackgroundColor = Settings.Instance.Theme.StatusBg;
            UpdateLayout();
            Studio.Instance.RecalculateLayout();
        };
        Settings.FontChanged += () => {
            label.Font = FontManager.StatusFont;
            UpdateLayout();
            Studio.Instance.RecalculateLayout();
        };
        
        var layout = new PixelLayout();
        layout.Add(label, 0, 0);
        
        const int popoutPaddingX = 10;
        const int popoutPaddingY = 0;
        var popoutButton = new PopoutButton();
        layout.Add(popoutButton, ClientSize.Width - popoutButton.Width - popoutPaddingX, popoutPaddingY);
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
                MenuUtils.CreateAction("Reconnect Studio and Celeste", Application.Instance.CommonModifier | Keys.Shift | Keys.D, () => CommunicationWrapper.ForceReconnect()),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Copy Custom Info Template to Clipboard", Keys.None, () => CommunicationWrapper.CopyCustomInfoTemplateToClipboard()),
                MenuUtils.CreateAction("Set Custom Info Template from Clipboard", Keys.None, () => CommunicationWrapper.SetCustomInfoTemplateFromClipboard()),
                MenuUtils.CreateAction("Clear Custom Info Template", Keys.None, () => CommunicationWrapper.ClearCustomInfoTemplate()),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Clear Watch Entity Info", Keys.None, () => CommunicationWrapper.ClearWatchEntityInfo()),
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
        
        Application.Instance.InvokeAsync(() => {
            int oldLineCount = label.Text.Split(["\n", "\r", "\n\r", Environment.NewLine], StringSplitOptions.None).Length;
            label.Text = $"{frameInfo}{Environment.NewLine}" + (CommunicationWrapper.Connected && CommunicationWrapper.GameInfo is { } gameInfo
                ? gameInfo.Trim()
                : DisconnectedText);
            int newLineCount = label.Text.Split(["\n", "\r", "\n\r", Environment.NewLine], StringSplitOptions.None).Length;
            
            if (oldLineCount != newLineCount) {
                UpdateLayout();
                Studio.Instance.RecalculateLayout();
            }
        });
    }
    
    protected override void OnMouseDown(MouseEventArgs e) {
        if (e.Buttons.HasFlag(MouseButtons.Alternate))
            ContextMenu.Show();
        
        base.OnMouseDown(e);
    }
}