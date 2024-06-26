using System;
using Eto.Forms;

namespace CelesteStudio.WPF;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            new Application(Eto.Platforms.Wpf).Run(new Studio());
        } catch (Exception ex) {
            ErrorLog.Write(ex);
            ErrorLog.Open();
        }
    }
}