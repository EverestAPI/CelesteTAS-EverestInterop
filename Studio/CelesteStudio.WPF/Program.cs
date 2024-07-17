using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using CelesteStudio.Communication;
using Dark.Net;
using Dark.Net.Wpf;
using Eto.Forms;
using Eto.Wpf;
using Eto.Wpf.Forms;
using Eto.Wpf.Forms.Controls;
using Eto.Wpf.Forms.Menu;
using Eto.Wpf.Forms.ToolBar;

namespace CelesteStudio.WPF;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            Settings.ThemeChanged += () => UpdateTheme(Settings.Instance.Theme.DarkMode);

            var app = new Application(Eto.Platforms.Wpf);
            var studio = new Studio(windowCreationCallback: window => {
                ApplyTheme(window, Settings.Instance.Theme.DarkMode);
                Settings.ThemeChanged += () => ApplyTheme(window, Settings.Instance.Theme.DarkMode);
            });

            UpdateTheme(Settings.Instance.Theme.DarkMode);
            
            studio.PreLoad += (_, _) => ApplyTheme(studio, Settings.Instance.Theme.DarkMode);
            Settings.ThemeChanged += () => ApplyTheme(studio, Settings.Instance.Theme.DarkMode);

            app.Run(studio);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            ErrorLog.Write(ex);
            ErrorLog.Open();
        }

        // Ensure the communication is stopped
        try {
            CommunicationWrapper.Stop();
        } catch (Exception) {
            // Just stop the process
            Environment.Exit(0);
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

    // Called via Reflection from Util/Extensions.cs
    public static void FixScrollable(Scrollable scrollable) {
        var handler = (ScrollableHandler)scrollable.Handler;

        if (Settings.Instance.Theme.DarkMode) {
            var appHandler = (ApplicationHandler)Application.Instance.Handler;
            handler.Control.BorderBrush = (SolidColorBrush)appHandler.Control.FindResource("PanelBorderBrush");
        } else {
            handler.Control.SetEtoBorderType(BorderType.Bezel);
        }
    }
}