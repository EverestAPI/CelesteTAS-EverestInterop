using System;
using Eto.Forms;

namespace CelesteStudio.GTK;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        new Application(Eto.Platforms.Gtk).Run(new Studio());
    }
}