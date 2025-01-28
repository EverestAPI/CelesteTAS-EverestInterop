using System.Diagnostics;
using System.Runtime.InteropServices;
using System;

namespace StudioCommunication.Util;

public static class ProcessHelper
{
    public static Process? OpenInDefaultApp(string path) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return Process.Start("xdg-open", [path]);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return Process.Start("open", [path]);
        } else {
            throw new NotImplementedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
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
        if (process.MainWindowHandle != IntPtr.Zero) {
            PostMessage(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;
}
