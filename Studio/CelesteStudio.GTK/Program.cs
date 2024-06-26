using System;
using Eto.Forms;

namespace CelesteStudio.GTK;

class Program {
    [STAThread]
    public static void Main(string[] args) {
        new Application(Eto.Platforms.Gtk).Run(new Studio());
    }
}