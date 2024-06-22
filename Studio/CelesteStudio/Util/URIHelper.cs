using System.Diagnostics;
using System.Runtime.InteropServices;
using Eto;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class URIHelper
{
    public static void OpenInBrowser(string url) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}"));
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Process.Start("xdg-open", url);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("open", url);
        } else {
            MessageBox.Show("Cannot open URL since platform is not recognized", MessageBoxType.Error);
        }
    }
}