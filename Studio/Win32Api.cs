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
}