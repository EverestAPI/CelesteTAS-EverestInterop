using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
        
        // create menu
        Menu = new MenuBar {
            Items = {
                // File submenu
                new SubMenuItem {Text = "&File", Items = {clickMe}},
                // new SubMenuItem { Text = "&Edit", Items = { /* commands/items */ } },
                // new SubMenuItem { Text = "&View", Items = { /* commands/items */ } },
            },
            ApplicationItems = {
                // application (OS X) or file menu (others)
                new ButtonMenuItem {Text = "&Preferences..."},
            },
            QuitItem = quitCommand,
            AboutItem = aboutCommand
        };
        
        // create toolbar			
        ToolBar = new ToolBar {Items = {clickMe}};
    }
}