using System;
using CelesteStudio.Communication;
using CelesteStudio.Controls;
using Eto.Forms;

namespace CelesteStudio.GTK;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            var platform = new Eto.GtkSharp.Platform();
            platform.Add<SkiaDrawable.IHandler>(() => new SkiaDrawableHandler());

            new Application(platform).Run(new Studio(args, _ => {}));
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
}
