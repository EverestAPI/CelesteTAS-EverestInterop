using System.Diagnostics;
using System.Runtime.InteropServices;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class ProcessHelper
{
    public static void OpenInDefaultApp(string path) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Process.Start("xdg-open", [path]);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("open", [path]);
        } else {
            MessageBox.Show($"Cannot open '{path}' in it's default app, since platform is not recognized", MessageBoxType.Error);
        }
    }
}
