using System;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio;

public class GameInfoPanel : Panel {
    private const string DisconnectedText = "Searching...";
    
    public GameInfoPanel() {
        var label = new Label {
            Text = DisconnectedText,
            TextColor = Settings.Instance.Theme.StatusFg,
            Font = new(new FontFamily("JetBrains Mono"), 9.0f),
        };
        
        BackgroundColor = Settings.Instance.Theme.StatusBg;
        Settings.ThemeChanged += () => {
            label.TextColor = Settings.Instance.Theme.StatusFg;
            BackgroundColor = Settings.Instance.Theme.StatusBg;
        };
        
        Padding = 5;
        Content = label;
        ContextMenu = new ContextMenu {
            Items = {
                MenuUtils.CreateAction("Copy Game Info to Clipboard", Application.Instance.CommonModifier | Keys.Shift | Keys.C, () => {
                    if (Studio.CelesteService.Server.GetDataFromGame(GameDataType.ExactGameInfo) is { } exactGameInfo) {
                        Clipboard.Instance.Clear();
                        Clipboard.Instance.Text = exactGameInfo;
                    }
                }),
                MenuUtils.CreateAction("Reconnect Studio and Celeste", Application.Instance.CommonModifier | Keys.Shift | Keys.D, () => Studio.CelesteService.Server.ExternalReset()),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Copy Custom Info Template to Clipboard", Keys.None, () => Studio.CelesteService.CopyCustomInfoTemplateToClipboard()),
                MenuUtils.CreateAction("Set Custom Info Template from Clipboard", Keys.None, () => Studio.CelesteService.SetCustomInfoTemplateFromClipboard()),
                MenuUtils.CreateAction("Clear Custom Info Template", Keys.None, () => Studio.CelesteService.ClearCustomInfoTemplate()),
                new SeparatorMenuItem(),
                MenuUtils.CreateAction("Clear Watch Entity Info", Keys.None, () => Studio.CelesteService.ClearWatchEntityInfo()),
            }
        };
        
        Studio.CelesteService.Server.StateUpdated += _ => Application.Instance.InvokeAsync(UpdateGameInfo);
        Studio.CelesteService.Server.Reset += () => Application.Instance.InvokeAsync(UpdateGameInfo);
        
        Settings.Changed += () => Visible = Settings.Instance.ShowGameInfo;
        
        void UpdateGameInfo() {
            int oldLineCount = label.Text.Split(["\n", "\r", "\n\r", Environment.NewLine], StringSplitOptions.None).Length;
            label.Text = Studio.CelesteService.Connected ? Studio.CelesteService.State.GameInfo.Trim() : DisconnectedText;
            int newLineCount = label.Text.Split(["\n", "\r", "\n\r", Environment.NewLine], StringSplitOptions.None).Length;
            
            if (oldLineCount != newLineCount)
                UpdateLayout();
            
            Studio.Instance.RecalculateLayout();
        }
    }
    
    protected override void OnMouseDown(MouseEventArgs e) {
        if (e.Buttons.HasFlag(MouseButtons.Alternate))
            ContextMenu.Show();
        
        base.OnMouseDown(e);
    }
}