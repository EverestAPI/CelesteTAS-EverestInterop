using System;
using Eto.Forms;

namespace CelesteStudio.Mac;

public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        try {
            new Application(Eto.Platforms.Mac64).Run(new Studio());
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            ErrorLog.Write(ex);
            ErrorLog.Open();
        }
    }
}