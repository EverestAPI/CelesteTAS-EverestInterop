using System;
using System.Runtime.InteropServices;

namespace CelesteStudio;

public static class Win32Api {
    [DllImport("user32.dll", EntryPoint = "OpenClipboard", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    public static void UnlockClipboard() {
        OpenClipboard(new IntPtr(0));
        EmptyClipboard();
        CloseClipboard();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static bool UseImmersiveDarkMode(IntPtr hwnd, bool enabled) {
        try {
            int useImmersiveDarkMode = enabled ? 1 : 0;
            bool success = DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) == 0;
            return success;
        } catch {
            return false;
        }
    }
}