using System.Diagnostics;
using System.Runtime.InteropServices;
using Eto;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class ProcessHelper
{
    public static void OpenInBrowser(string url) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}"));
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Process.Start("xdg-open", url);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("open", url);
        } else {
            MessageBox.Show("Cannot open URL, since platform is not recognized", MessageBoxType.Error);
        }
    }
    
    public static void OpenInDefaultApp(string filePath) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {filePath}"));
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Process.Start("xdg-open", filePath);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("open", filePath);
        } else {
            MessageBox.Show("Cannot open file in editor, since platform is not recognized", MessageBoxType.Error);
        }
    }
}