using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Dark.Net;
using Dark.Net.Wpf;
using Eto.Forms;
using Eto.Wpf.Forms;
using Eto.Wpf.Forms.Menu;
using Eto.Wpf.Forms.ToolBar;

namespace CelesteStudio.WPF;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            var app = new Application(Eto.Platforms.Wpf);
            var studio = new Studio(window => {
                var nativeWindow = ((IWpfWindow)window.Handler).Control;
                DarkNet.Instance.SetWindowThemeWpf(nativeWindow, Settings.Instance.ThemeType == ThemeType.Dark ? Theme.Dark : Theme.Light);
            });

            DarkNet.Instance.SetCurrentProcessTheme(Settings.Instance.ThemeType == ThemeType.Dark ? Theme.Dark : Theme.Light);
            
            studio.PreLoad += (_, _) => {
                ApplyTheme(studio, Settings.Instance.ThemeType == ThemeType.Dark);
            };
            Settings.ThemeChanged += () => {
                bool isDark = Settings.Instance.ThemeType == ThemeType.Dark;
                DarkNet.Instance.SetCurrentProcessTheme(isDark ? Theme.Dark : Theme.Light);
                ApplyTheme(studio, isDark);
            };

            app.Run(studio);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            ErrorLog.Write(ex);
            ErrorLog.Open();
        }
    }

    private static void ApplyTheme(Studio studio, bool isDark) {
        var formHandler = (FormHandler)studio.Handler;
    
        DarkNet.Instance.SetWindowThemeWpf(formHandler.Control, isDark ? Theme.Dark : Theme.Light);

        var appHandler = (ApplicationHandler)Application.Instance.Handler;
        var assemblyName = typeof(Program).Assembly.GetName().Name;
        
        appHandler.Control.Resources.MergedDictionaries.Clear();
        if (isDark) {
            appHandler.Control.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/{assemblyName};component/Themes/ColourDictionaries/DeepDark.xaml", UriKind.RelativeOrAbsolute) });
            appHandler.Control.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/{assemblyName};component/Themes/ControlColours.xaml", UriKind.RelativeOrAbsolute) });
            appHandler.Control.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/{assemblyName};component/Themes/Controls.xaml", UriKind.RelativeOrAbsolute) });
        }
    }
}