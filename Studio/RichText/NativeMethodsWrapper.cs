using System;

namespace CelesteStudio.RichText {
internal class NativeMethodsWrapper : NativeMethods {
    public new static IntPtr ImmGetContext(IntPtr hWnd) {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return IntPtr.Zero;
        } else {
            return NativeMethods.ImmGetContext(hWnd);
        }
    }

    public new static IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC) {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return IntPtr.Zero;
        } else {
            return NativeMethods.ImmAssociateContext(hWnd, hIMC);
        }
    }

    public static new bool CreateCaret(IntPtr hWnd, int hBitmap, int nWidth, int nHeight) {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return true;
        } else {
            return NativeMethods.CreateCaret(hWnd, hBitmap, nWidth, nHeight);
        }
    }

    public static new bool SetCaretPos(int x, int y) {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return true;
        } else {
            return NativeMethods.SetCaretPos(x, y);
        }
    }

    public static new bool DestroyCaret() {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return true;
        } else {
            return NativeMethods.DestroyCaret();
        }
    }

    public static new bool ShowCaret(IntPtr hWnd) {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return true;
        } else {
            return NativeMethods.ShowCaret(hWnd);
        }
    }

    public static new bool HideCaret(IntPtr hWnd) {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return true;
        } else {
            return NativeMethods.HideCaret(hWnd);
        }
    }
}
}