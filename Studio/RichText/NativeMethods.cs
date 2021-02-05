using System;
using System.Runtime.InteropServices;

namespace CelesteStudio.RichText {
internal class NativeMethods {
    [DllImport("Imm32.dll")]
    internal static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("Imm32.dll")]
    internal static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("User32.dll")]
    internal static extern bool CreateCaret(IntPtr hWnd, int hBitmap, int nWidth, int nHeight);

    [DllImport("User32.dll")]
    internal static extern bool SetCaretPos(int x, int y);

    [DllImport("User32.dll")]
    internal static extern bool DestroyCaret();

    [DllImport("User32.dll")]
    internal static extern bool ShowCaret(IntPtr hWnd);

    [DllImport("User32.dll")]
    internal static extern bool HideCaret(IntPtr hWnd);
}
}