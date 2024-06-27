using System;
using Eto.Forms;

namespace CelesteStudio.GTK;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            new Application(Eto.Platforms.Gtk).Run(new Studio());
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            ErrorLog.Write(ex);
            ErrorLog.Open();
        }
    }
}