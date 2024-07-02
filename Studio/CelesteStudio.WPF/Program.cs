using System;
using System.Runtime.InteropServices;
using Dark.Net;
using Eto.Forms;
using Eto.Wpf.Forms;

namespace CelesteStudio.WPF;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            var app = new Application(Eto.Platforms.Wpf);
            var studio = new Studio();
            
            DarkNet.Instance.SetCurrentProcessTheme(Theme.Dark);
            
            var window = ((FormHandler)studio.Handler).Control;
            studio.PreLoad += (_, _) => DarkNet.Instance.SetWindowThemeWpf(window, Settings.Instance.ThemeType == ThemeType.Dark ? Theme.Dark : Theme.Light);
            Settings.ThemeChanged += () => DarkNet.Instance.SetWindowThemeWpf(window, Settings.Instance.ThemeType == ThemeType.Dark ? Theme.Dark : Theme.Light);
            
            app.Run(studio);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            ErrorLog.Write(ex);
            ErrorLog.Open();
        }
    }
}