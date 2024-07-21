using System;
using CelesteStudio.Communication;
using Eto.Forms;

namespace CelesteStudio.GTK;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            new Application(Eto.Platforms.Gtk).Run(new Studio(_ => {}));
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
