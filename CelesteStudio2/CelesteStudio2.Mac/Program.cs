using System;
using Eto.Forms;

namespace CelesteStudio2.Mac {
    class Program {
        [STAThread]
        public static void Main(string[] args) {
            new Application(Eto.Platforms.Mac64).Run(new MainForm());
        }
    }
}