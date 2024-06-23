using System;
using Eto.Forms;

namespace CelesteStudio.WPF;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        new Application(Eto.Platforms.Wpf).Run(new Studio());
    }
}