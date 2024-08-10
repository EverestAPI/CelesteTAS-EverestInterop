using System.Diagnostics;
using System.Runtime.InteropServices;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class ProcessHelper
{
    public static void OpenInDefaultApp(string argument) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Process.Start("cmd", ["/c", "start", argument]);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Process.Start("xdg-open", [argument]);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("open", [argument]);
        } else {
            MessageBox.Show($"Cannot open '{argument}' in it's default app, since platform is not recognized", MessageBoxType.Error);
        }
    }
}