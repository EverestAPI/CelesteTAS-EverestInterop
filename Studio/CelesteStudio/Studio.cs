using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.Drawing;
using Eto.Forms.ThemedControls;
using StudioCommunication;

namespace CelesteStudio;

public partial class Studio : Form {
    public static Studio Instance;
    public static Version Version { get; private set; }
    
    public static CelesteService CelesteService = new();
    
    private States tasStates;
    
    private bool DisableTyping => tasStates.HasFlag(States.Enable) && 
                                  !tasStates.HasFlag(States.FrameStep) && 
                                  StudioCommunicationBase.Initialized;
    
    private string TitleBarText =>
        (string.IsNullOrEmpty(CurrentFileName) ? "Celeste.tas" : Path.GetFileName(CurrentFileName))
        + " - Studio v"
        + Version.ToString(3)
        + (string.IsNullOrEmpty(CurrentFileName) ? string.Empty : "   " + CurrentFileName);
    
    private string CurrentFileName {
        // get => richText.CurrentFileName;
        // set => richText.CurrentFileName = value;
        get => "Test.tas";
        set {}
    }
    
    private Label chapterTimeLabel;
    
    public Studio() {
        Instance = this;
        Version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = TitleBarText;
        
        MinimumSize = new Size(200, 200);
        
        var scrollable = new Scrollable() {
            Width = 300,
            Height = 500,
        };
        var editor = new Editor(scrollable);
        scrollable.Content = editor;
        
        Content = new StackLayout {
            Padding = 10,
            Items = {
                "Hello World!!!",
                // add more controls here
                (chapterTimeLabel = new Label() {
                    Text = "hai",
                }),
                "aa",
                //new TextArea() { Height = 300 },
                scrollable
            }
        };
        
        CelesteService.Server.StateUpdated += state =>
        {
            Application.Instance.Invoke(() => {
                chapterTimeLabel.Text = state.ChapterTime;    
            });
        };
        
        // create a few commands that can be used for the menu and toolbar
        var clickMe = new Command {MenuText = "Click Me!", ToolBarText = "Click Me!"};
        clickMe.Executed += (sender, e) => MessageBox.Show(this, "I was clicked!");
        
        var quitCommand = new Command {MenuText = "Quit", Shortcut = Application.Instance.CommonModifier | Keys.Q};
        quitCommand.Executed += (sender, e) => Application.Instance.Quit();
        
        var aboutCommand = new Command {MenuText = "About..."};
        aboutCommand.Executed += (sender, e) => new AboutDialog().ShowDialog(this);
        
        var homeCommand = new Command {MenuText = "Home"};
        homeCommand.Executed += (sender, e) => URIHelper.OpenInBrowser("https://github.com/EverestAPI/CelesteTAS-EverestInterop");
        
        static MenuItem CreateToggle(string text, Func<bool> getFn, Action toggleFn)
        {
            var cmd = new CheckCommand { MenuText = text };
            cmd.Executed += (_, _) => toggleFn();
            
            // TODO: Convert to CheckMenuItem
            return new ButtonMenuItem(cmd);
        }
        
        // create menu
        Menu = new MenuBar {
            Items = {
                // File submenu
                new SubMenuItem {Text = "&File", Items = {clickMe}},
                new SubMenuItem {Text = "&Settings", Items = {clickMe}},
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
                    // CreateToggleCommand("Position Decimals", CelesteService.ToggleHitboxes),
                    // CreateToggleCommand("Speed Decimals", CelesteService.ToggleHitboxes),
                    // CreateToggleCommand("Velocity Decimals", CelesteService.ToggleHitboxes),
                    // CreateToggleCommand("Angle Decimals", CelesteService.ToggleHitboxes),
                    // CreateToggleCommand("Custom Info Decimals", CelesteService.ToggleHitboxes),
                    // CreateToggleCommand("Subpixel Indicator Decimals", CelesteService.ToggleHitboxes),
                    CreateToggle("Unit of Speed", CelesteService.GetSpeedUnit, CelesteService.ToggleSpeedUnit),
                    new SeparatorMenuItem(),
                    // CreateToggleCommand("Fast Forward Speed", CelesteService.ToggleHitboxes),
                    // CreateToggleCommand("Slow Forward Speed", CelesteService.ToggleHitboxes),
                }},
            },
            ApplicationItems = {
                // application (OS X) or file menu (others)
                new ButtonMenuItem {Text = "&Preferences..."},
            },
            QuitItem = quitCommand,
            AboutItem = aboutCommand
        };
        
        Menu.HelpItems.Insert(0, homeCommand);
        
        // create toolbar			
        ToolBar = new ToolBar {Items = {clickMe}};
    }
}