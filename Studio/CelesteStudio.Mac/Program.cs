using System;
using CelesteStudio.Communication;
using Eto.Forms;

namespace CelesteStudio.Mac;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            new Application(Eto.Platforms.Mac64).Run(new Studio(_ => {}));
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
