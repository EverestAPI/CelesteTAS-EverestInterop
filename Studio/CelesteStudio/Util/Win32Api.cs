using System;
using System.Runtime.InteropServices;

namespace CelesteStudio.Util;

public static class Win32Api {
    
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();
    
    [DllImport("kernel32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd);
    
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    
    public static void SetDarkTitleBar(bool enabled) {
        try {
            uint currentProcess = GetCurrentProcessId();
            EnumWindows((hWnd, _) => {
                // Check if this is our window
                try {
                    uint windowProcess = GetWindowThreadProcessId(hWnd);
                    if (currentProcess == windowProcess) {
                        int useImmersiveDarkMode = enabled ? 1 : 0;
                        DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
                        
                        return false;
                    }
                } catch {
                    // ignore
                }
                
                return true;
            }, IntPtr.Zero);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to set immersive dark mode: {ex}");
        }
    }
}