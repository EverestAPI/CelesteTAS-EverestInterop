using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.ApplicationServices;

namespace CelesteStudio {
    public class Program : WindowsFormsApplicationBase {
        private Program() {
            IsSingleInstance = true;
            StartupNextInstance += OnStartupNextInstance;
        }

        [STAThread]
        public static void Main(string[] args) {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            if (Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) is { } exeDir) {
                Directory.SetCurrentDirectory(exeDir);
            }

            new Program().Run(args);
        }

        private static void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e) {
            Exception exception = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
            if (exception.GetType().FullName == "System.Configuration.ConfigurationErrorsException") {
                MessageBox.Show("Your configuration file is corrupted and will be deleted automatically, please try to launch celeste studio again.",
                    "Configuration Errors Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
                string configFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Celeste_Studio");
                if (Directory.Exists(configFolder)) {
                    Directory.Delete(configFolder, true);
                }
            } else {
                ErrorLog.Write(exception);
                ErrorLog.Open();
            }

            Application.Exit();
        }

        private void OnStartupNextInstance(object sender, StartupNextInstanceEventArgs e) {
            (MainForm as Studio)?.TryOpenFile(e.CommandLine.ToArray());
        }

        protected override void OnCreateMainForm() {
            MainForm = new Studio(CommandLineArgs.ToArray());
        }
    }
}