using System;
using System.Linq;
using System.Text;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Editing;

public class GameInfoPanel : Panel {
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
        
        Padding = 5;
        Content = label;
        ContextMenu = new ContextMenu {
            Items = {
                MenuUtils.CreateAction("Copy Game Info to Clipboard", Application.Instance.CommonModifier | Keys.Shift | Keys.C, () => {
                    if (Studio.CommunicationWrapper.GetExactGameInfo() is var exactGameInfo && !string.IsNullOrWhiteSpace(exactGameInfo)) {
                        Clipboard.Instance.Clear();
                        Clipboard.Instance.Text = exactGameInfo;
                    }
                }),
                MenuUtils.CreateAction("Reconnect Studio and Celeste", Application.Instance.CommonModifier | Keys.Shift | Keys.D, () => Studio.CommunicationWrapper.ForceReconnect()),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Copy Custom Info Template to Clipboard", Keys.None, () => Studio.CommunicationWrapper.CopyCustomInfoTemplateToClipboard()),
                MenuUtils.CreateAction("Set Custom Info Template from Clipboard", Keys.None, () => Studio.CommunicationWrapper.SetCustomInfoTemplateFromClipboard()),
                MenuUtils.CreateAction("Clear Custom Info Template", Keys.None, () => Studio.CommunicationWrapper.ClearCustomInfoTemplate()),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Clear Watch Entity Info", Keys.None, () => Studio.CommunicationWrapper.ClearWatchEntityInfo()),
            }
        };
        
        Studio.CommunicationWrapper.StateUpdated += (prevState, state) => {
            if (!Settings.Instance.ShowGameInfo || prevState.GameInfo == state.GameInfo)
                return;
            
            if (prevState.TotalFrames != state.TotalFrames)
                TotalFrames = state.TotalFrames;
            
            UpdateGameInfo();
        };
        Studio.CommunicationWrapper.ConnectionChanged += UpdateGameInfo;
    }
        
    public void UpdateGameInfo() {
        var frameInfo = new StringBuilder();
        if (Studio.CommunicationWrapper.State.CurrentFrameInTas > 0) {
            frameInfo.Append($"{Studio.CommunicationWrapper.State.CurrentFrameInTas}/");
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
            label.Text = $"{frameInfo}{Environment.NewLine}" + (Studio.CommunicationWrapper.Connected && Studio.CommunicationWrapper.State.GameInfo is { } gameInfo
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