using System;
using Eto.Forms;

namespace CelesteStudio2.Wpf {
    class Program {
        [STAThread]
        public static void Main(string[] args) {
            new Application(Eto.Platforms.Wpf).Run(new MainForm());
        }
    }
}