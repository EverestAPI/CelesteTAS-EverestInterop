using System;
using Eto.Forms;

namespace CelesteStudio.WPF;

class Program {
    [STAThread]
    public static void Main(string[] args) {
        new Application(Eto.Platforms.Wpf).Run(new Studio());
    }
}