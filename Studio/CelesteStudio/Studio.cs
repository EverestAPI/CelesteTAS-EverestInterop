using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.Drawing;
using Eto.Forms.ThemedControls;
using StudioCommunication;

namespace CelesteStudio;

public sealed partial class Studio : Form {
    public static Studio Instance;
    public static Version Version { get; private set; }
    
    public static CelesteService CelesteService = new();

    private Editor? editor = null;
    private string TitleBarText => $"{editor?.Document.FileName ?? "Celeste.tas"}{((editor?.Document.Dirty ?? false) ? "*" : string.Empty)} - Studio v{Version.ToString(3)}   {editor?.Document.FilePath ?? string.Empty}";
    
    public Studio() {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version!;
        
        var scrollable = new Scrollable {
            Width = 300,
            Height = 500,
        };
        editor = new Editor(Document.CreateBlank(), scrollable);
        scrollable.Content = editor;
        
        Content = new StackLayout {
            Padding = 0,
            Items = { scrollable }
        };

        const int ExtraHeight = 30; // The horizontal scrollbar is not included in the Size? TODO: Figure out correct value for other platforms
        SizeChanged += (_, _) => scrollable.Size = new Size(Size.Width, Size.Height - ExtraHeight);
        
        Menu = CreateMenu();
        Title = TitleBarText;
        editor.Document.TextChanged += _ => Title = TitleBarText;
    }
    
    private MenuBar CreateMenu()
    {
        static MenuItem CreateToggle(string text, Func<bool> getFn, Action toggleFn)
        {
            var cmd = new CheckCommand { MenuText = text };
            cmd.Executed += (_, _) => toggleFn();
            
            // TODO: Convert to CheckMenuItem
            return new ButtonMenuItem(cmd);
        }
        
        static MenuItem CreateNumberInput<T>(string text, Func<T> getFn, Action<T> setFn, T minValue, T maxValue, T step) where T : INumber<T>
        {
            var cmd = new Command { MenuText = text };
            cmd.Executed += (_, _) => setFn(DialogUtil.ShowNumberInputDialog(text, getFn(), minValue, maxValue, step));
            
            return new ButtonMenuItem(cmd);
        }
        
        const int MinDecimals = 2;
        const int MaxDecimals = 12;
        const int MinFastForwardSpeed = 2;
        const int MaxFastForwardSpeed = 30;
        const float MinSlowForwardSpeed = 0.1f;
        const float MaxSlowForwardSpeed = 0.9f;
        
        var quitCommand = new Command {MenuText = "Quit", Shortcut = Application.Instance.CommonModifier | Keys.Q};
        quitCommand.Executed += (sender, e) => Application.Instance.Quit();
        
        var aboutCommand = new Command {MenuText = "About..."};
        aboutCommand.Executed += (sender, e) => new AboutDialog().ShowDialog(this);
        
        var homeCommand = new Command {MenuText = "Home"};
        homeCommand.Executed += (sender, e) => URIHelper.OpenInBrowser("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
        
        var menu = new MenuBar {
            Items = {
                // File submenu
                new SubMenuItem {Text = "&File", Items = {}},
                new SubMenuItem {Text = "&Settings", Items = {}},
                new SubMenuItem {Text = "&Toggles", Items =
                {
                    CreateToggle("&Hitboxes", CelesteService.GetHitboxes, CelesteService.ToggleHitboxes),
                    CreateToggle("&Trigger Hitboxes", CelesteService.GetTriggerHitboxes, CelesteService.ToggleTriggerHitboxes),
                    CreateToggle("Unloaded Room Hitboxes", CelesteService.GetUnloadedRoomsHitboxes, CelesteService.ToggleUnloadedRoomsHitboxes),
                    CreateToggle("Camera Hitboxes", CelesteService.GetCameraHitboxes, CelesteService.ToggleCameraHitboxes),
                    CreateToggle("&Simplified Hitboxes", CelesteService.GetSimplifiedHitboxes, CelesteService.ToggleSimplifiedHitboxes),
                    CreateToggle("&Actual Collide Hitboxes", CelesteService.GetActualCollideHitboxes, CelesteService.ToggleActualCollideHitboxes),
                    new SeparatorMenuItem(),
                    CreateToggle("&Simplified &Graphics", CelesteService.GetSimplifiedGraphics, CelesteService.ToggleSimplifiedGraphics),
                    CreateToggle("Game&play", CelesteService.GetGameplay, CelesteService.ToggleGameplay),
                    new SeparatorMenuItem(),
                    CreateToggle("&Center Camera", CelesteService.GetCenterCamera, CelesteService.ToggleCenterCamera),
                    CreateToggle("Center Camera Horizontally Only", CelesteService.GetCenterCameraHorizontallyOnly, CelesteService.ToggleCenterCameraHorizontallyOnly),
                    new SeparatorMenuItem(),
                    CreateToggle("&Info HUD", CelesteService.GetInfoHud, CelesteService.ToggleInfoHud),
                    CreateToggle("TAS Input Info", CelesteService.GetInfoTasInput, CelesteService.ToggleInfoTasInput),
                    CreateToggle("Game Info", CelesteService.GetInfoGame, CelesteService.ToggleInfoGame),
                    CreateToggle("Watch Entity Info", CelesteService.GetInfoWatchEntity, CelesteService.ToggleInfoWatchEntity),
                    CreateToggle("Custom Info", CelesteService.GetInfoCustom, CelesteService.ToggleInfoCustom),
                    CreateToggle("Subpixel Indicator", CelesteService.GetInfoSubpixelIndicator, CelesteService.ToggleInfoSubpixelIndicator),
                    new SeparatorMenuItem(),
                    CreateNumberInput("Position Decimals", CelesteService.GetPositionDecimals, CelesteService.SetPositionDecimals, MinDecimals, MaxDecimals, 1),
                    CreateNumberInput("Speed Decimals", CelesteService.GetSpeedDecimals, CelesteService.SetSpeedDecimals, MinDecimals, MaxDecimals, 1),
                    CreateNumberInput("Velocity Decimals", CelesteService.GetVelocityDecimals, CelesteService.SetVelocityDecimals, MinDecimals, MaxDecimals, 1),
                    CreateNumberInput("Angle Decimals", CelesteService.GetAngleDecimals, CelesteService.SetAngleDecimals, MinDecimals, MaxDecimals, 1),
                    CreateNumberInput("Custom Info Decimals", CelesteService.GetCustomInfoDecimals, CelesteService.SetCustomInfoDecimals, MinDecimals, MaxDecimals, 1),
                    CreateNumberInput("Subpixel Indicator Decimals", CelesteService.GetSubpixelIndicatorDecimals, CelesteService.SetSubpixelIndicatorDecimals, MinDecimals, MaxDecimals, 1),
                    CreateToggle("Unit of Speed", CelesteService.GetSpeedUnit, CelesteService.ToggleSpeedUnit),
                    new SeparatorMenuItem(),
                    CreateNumberInput("Fast Forward Speed", CelesteService.GetFastForwardSpeed, CelesteService.SetFastForwardSpeed, MinFastForwardSpeed, MaxFastForwardSpeed, 1),
                    CreateNumberInput("Slow Forward Speed", CelesteService.GetSlowForwardSpeed, CelesteService.SetSlowForwardSpeed, MinSlowForwardSpeed, MaxSlowForwardSpeed, 0.1f),
                }},
            },
            ApplicationItems = {
                // application (OS X) or file menu (others)
                new ButtonMenuItem {Text = "&Preferences..."},
            },
            QuitItem = quitCommand,
            AboutItem = aboutCommand
        };
        
        menu.HelpItems.Insert(0, homeCommand); // The "About" is automatically inserted
        
        return menu;
    }
}