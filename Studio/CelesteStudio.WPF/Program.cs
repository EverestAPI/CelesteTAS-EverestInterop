using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Dark.Net;
using Dark.Net.Wpf;
using Eto.Forms;
using Eto.Wpf;
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
                ApplyTheme(window, Settings.Instance.ThemeType == ThemeType.Dark);
                Settings.ThemeChanged += () => ApplyTheme(window, Settings.Instance.ThemeType == ThemeType.Dark);
            });

            DarkNet.Instance.SetCurrentProcessTheme(Settings.Instance.ThemeType == ThemeType.Dark ? Theme.Dark : Theme.Light);
            
            studio.PreLoad += (_, _) => ApplyTheme(studio, Settings.Instance.ThemeType == ThemeType.Dark);
            Settings.ThemeChanged += () => {
                bool isDark = Settings.Instance.ThemeType == ThemeType.Dark;
                UpdateTheme(isDark);
                ApplyTheme(studio, isDark);
            };

            UpdateTheme(Settings.Instance.ThemeType == ThemeType.Dark);

            app.Run(studio);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            ErrorLog.Write(ex);
            ErrorLog.Open();
        }
    }

    private static void UpdateTheme(bool isDark) {
        DarkNet.Instance.SetCurrentProcessTheme(isDark ? Theme.Dark : Theme.Light);

        var appHandler = (ApplicationHandler)Application.Instance.Handler;
        appHandler.Control.Resources.MergedDictionaries.Clear();
        if (isDark) {
            var assemblyName = typeof(Program).Assembly.GetName().Name;
            appHandler.Control.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/{assemblyName};component/Themes/ColourDictionaries/SoftDark.xaml", UriKind.RelativeOrAbsolute) });
            appHandler.Control.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/{assemblyName};component/Themes/ControlColours.xaml", UriKind.RelativeOrAbsolute) });
            appHandler.Control.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/{assemblyName};component/Themes/Controls.xaml", UriKind.RelativeOrAbsolute) });
        }
    }
    private static void ApplyTheme(Window window, bool isDark) {
        var assemblyName = typeof(Program).Assembly.GetName().Name;
        
        if (isDark) {
            var appHandler = (ApplicationHandler)Application.Instance.Handler;
            window.BackgroundColor = ((SolidColorBrush)appHandler.Control.FindResource("Window.Static.Background")).ToEtoColor();
        } else {
            window.BackgroundColor = Brushes.Transparent.ToEtoColor();
        }

        var nativeWindow = ((IWpfWindow)window.Handler).Control;
        var hwnd = new WindowInteropHelper(nativeWindow).Handle;
        // -Wpf works for Forms, -Raw works for Dialogs, so just apply both
        DarkNet.Instance.SetWindowThemeWpf(nativeWindow, isDark ? Theme.Dark : Theme.Light);
        DarkNet.Instance.SetWindowThemeRaw(hwnd, isDark ? Theme.Dark : Theme.Light);
    }
}