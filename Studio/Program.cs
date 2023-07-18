using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.ApplicationServices;
using StudioCommunication;

namespace CelesteStudio;

public class Program : WindowsFormsApplicationBase {
    private Program() {
        IsSingleInstance = false;
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

        if (PlatformUtils.Wine) {
            Console.SetOut(new StringWriter());
        }

        new Program().Run(args);
    }

    private static void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e) {
        Exception exception = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
        ErrorLog.Write(exception);
        ErrorLog.Open();
        Application.Exit();
    }

    protected override bool OnStartup(StartupEventArgs eventArgs) {
        // TODO fix this weird bug
        // if IsSingleInstance = true and celeste launch before studio the connection will fail, idnw...
        // so we just close the studio already launched
        if (!IsSingleInstance) {
            try {
                foreach (Process process in Process.GetProcessesByName("Celeste Studio")) {
                    if (process.Id != Process.GetCurrentProcess().Id) {
                        // trigger save settings
                        Hide(process.MainWindowHandle);
                        Restore(process.MainWindowHandle);
                        process.Kill();
                    }
                }
            } catch (Win32Exception) {
                MessageBox.Show("Celeste Studio does not support multiple instances, please close other instances manually.",
                    "Failure to close other studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        return base.OnStartup(eventArgs);
    }

    private void OnStartupNextInstance(object sender, StartupNextInstanceEventArgs e) {
        (MainForm as Studio)?.TryOpenFile(e.CommandLine.ToArray());
        if (MainForm.WindowState == FormWindowState.Minimized) {
            Restore(MainForm.Handle);
        } else {
            SetForegroundWindowEx(MainForm);
        }
    }

    protected override void OnCreateMainForm() {
        MainForm = new Studio(CommandLineArgs.ToArray());
    }

    [DllImport("user32.dll")]
    private static extern int ShowWindow(IntPtr hWnd, uint msg);

    [DllImport("User32.dll", CharSet = CharSet.Auto)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static void Restore(IntPtr hWnd) {
        const int restore = 0x09;
        ShowWindow(hWnd, restore);
    }

    private static void Hide(IntPtr hWnd) {
        const int hide = 0x00;
        ShowWindow(hWnd, hide);
    }

    private void SetForegroundWindowEx(Form form) {
        uint appThread = GetCurrentThreadId();
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);

        if (foregroundThread != appThread) {
            AttachThreadInput(foregroundThread, appThread, true);
            form.Activate();
            AttachThreadInput(foregroundThread, appThread, false);
        }
    }
}