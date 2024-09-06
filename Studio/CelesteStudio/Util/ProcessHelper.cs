using System.Diagnostics;
using System.Runtime.InteropServices;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class ProcessHelper
{
    public static void OpenInDefaultApp(string path) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Process.Start("xdg-open", [path]);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("open", [path]);
        } else {
            MessageBox.Show($"Cannot open '{path}' in it's default app, since platform is not recognized", MessageBoxType.Error);
        }
    }

    /// Sends a termination signal to the process to gracefully exit
    public static void Terminate(this Process process) {
        // Unix
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("kill", ["-s", "SIGINT", process.Id.ToString()]);
            return;
        }

        // Windows
        FreeConsole();
        AttachConsole((uint)process.Id);
        GenerateConsoleCtrlEvent(0, 0);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool FreeConsole();
}
