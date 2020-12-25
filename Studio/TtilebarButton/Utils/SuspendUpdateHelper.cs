using System;
using System.Windows.Forms;

namespace CelesteStudio.TtilebarButton.Utils
{
    internal static class SuspendUpdateHelper
    {
        private const int WM_SETREDRAW = 0x000B;


        public static void Suspend(Control control)
        {
            if (control.IsHandleCreated)
            {
                var msgSuspendUpdate = Message.Create(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

                var window = NativeWindow.FromHandle(control.Handle);
                window.DefWndProc(ref msgSuspendUpdate);
            }
        }
        public static void Resume(Control control, bool invalidate = true)
        {
            if (control.IsHandleCreated)
            {
                var wparam = new IntPtr(1);
                var msgResumeUpdate = Message.Create(control.Handle, WM_SETREDRAW, wparam, IntPtr.Zero);

                var window = NativeWindow.FromHandle(control.Handle);
                window.DefWndProc(ref msgResumeUpdate);

                if (invalidate)
                    control.Invalidate(true);
            }
        }
    }
}
