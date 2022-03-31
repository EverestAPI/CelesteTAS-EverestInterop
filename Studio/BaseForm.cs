using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static CelesteStudio.NativeMethods;

// https://stackoverflow.com/a/65863602 by @Reza Aghaei
namespace CelesteStudio;

public class BaseForm : Form {
    public event EventHandler NonClientMouseHover;
    public event EventHandler NonClientMouseLeave;
    public event EventHandler NonClientMouseMove;
    private TRACK_MOUSE_EVENT track = TRACK_MOUSE_EVENT.Empty;
    public Rectangle TitleRectangle => GetTitleBarRectangle(Handle);

    protected override void WndProc(ref Message m) {
        base.WndProc(ref m);
        if (m.Msg == WM_NCMOUSEMOVE) {
            track.hwndTrack = Handle;
            track.cbSize = (uint) Marshal.SizeOf(track);
            track.dwFlags = TME_HOVER | TME_LEAVE | TME_NONCLIENT;
            track.dwHoverTime = 500;
            TrackMouseEvent(ref track);
            NonClientMouseMove?.Invoke(this, EventArgs.Empty);
        } else if (m.Msg == WM_NCMOUSEHOVER) {
            NonClientMouseHover?.Invoke(this, EventArgs.Empty);
        } else if (m.Msg == WM_NCMOUSELEAVE) {
            NonClientMouseLeave?.Invoke(this, EventArgs.Empty);
        }
    }
}

public static class NativeMethods {
    public const int WM_NCMOUSEMOVE = 0xA0;
    public const int WM_NCMOUSEHOVER = 0x2A0;
    public const int WM_NCMOUSELEAVE = 0x2A2;
    public const int TME_HOVER = 0x1;
    public const int TME_LEAVE = 0x2;
    public const int TME_NONCLIENT = 0x10;

    [DllImport("user32.dll")]
    public static extern int TrackMouseEvent(ref TRACK_MOUSE_EVENT lpEventTrack);

    [DllImport("user32.dll")]
    public static extern bool GetTitleBarInfo(IntPtr hwnd, ref TITLEBARINFO pti);

    public static Rectangle GetTitleBarRectangle(IntPtr hwnd) {
        var info = new TITLEBARINFO {cbSize = (uint) Marshal.SizeOf(typeof(TITLEBARINFO))};
        GetTitleBarInfo(hwnd, ref info);
        return info.rcTitleBar.ToRectangle();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACK_MOUSE_EVENT {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
        public static readonly TRACK_MOUSE_EVENT Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TITLEBARINFO {
        public const int CCHILDREN_TITLEBAR = 5;
        public uint cbSize;
        public RECT rcTitleBar;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = CCHILDREN_TITLEBAR + 1)]
        public uint[] rgstate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left, Top, Right, Bottom;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
}