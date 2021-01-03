using System;
using System.Runtime.InteropServices;

// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace CelesteStudio.TtilebarButton.Utils
{
    /// <summary>
    ///     The native Win32 APIs used by the library.
    /// </summary>
    internal class Win32
    {
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint WS_EX_COMPOSITED = 0x02000000;
        public const uint WS_CLIPSIBLINGS = 0x4000000;
        public const uint WM_ACTIVATEAPP = 28;
        public const int WM_SIZE = 5;

        /// <summary>
        ///     The current Verison of windows.
        /// </summary>
        public static readonly int SystemVersion = Environment.OSVersion.Version.Major;
        public static readonly int SystemVersionMinor = Environment.OSVersion.Version.Minor;

        /// <summary>
        ///     Returns true when aero glass is enabled
        /// </summary>
        /// <returns></returns>
        public static bool DwmIsCompositionEnabled
        {
            get
            {
                // return ture if Windows supports aero and it's enabled.
                if (SystemVersion >= 6)
                    return DwmIsCompositionEnabled32();
                return false;
            }
        }


        /// <summary>
        ///     Returns a pointer to the Desktop window.
        /// </summary>
        /// <returns>Pointer to the desktop window.</returns>
        [DllImport("user32.dll", EntryPoint = "GetDesktopWindow")]
        public static extern IntPtr GetDesktopWindow();

        /// <summary>
        ///     Determines whether aero is enabled.
        /// </summary>
        /// <returns></returns>
        [DllImport("dwmapi.dll", EntryPoint = "DwmIsCompositionEnabled", PreserveSig = false)]
        private static extern bool DwmIsCompositionEnabled32();
    }
}
