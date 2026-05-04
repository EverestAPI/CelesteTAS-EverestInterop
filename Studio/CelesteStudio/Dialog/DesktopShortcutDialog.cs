using Eto.Forms;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace CelesteStudio.Dialog;

public class DesktopShortcutDialog : Dialog<bool?> {
    private DesktopShortcutDialog(Window parentWindow) {
        Title = "Install Desktop Shortcut";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            Items = {
                new Label {
                    Text = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                        ? "Would you like to create a desktop shortcut for Celeste Studio?\nThis is required to properly show a desktop icon on Wayland."
                        : "Would you like to create a desktop shortcut for Celeste Studio?"
                },
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = {
                        new Button((_, _) => Close(true)) { Text = "&Create Shortcut" },
                        new Button((_, _) => Close(null)) { Text = "&Not now" },
                        new Button((_, _) => Close(false)) { Text = "&Don't ask this again" },
                    }
                },
            },
        };

        Studio.RegisterDialog(this, parentWindow);
    }
    
    public static void Show(Control owner) {
        bool? result = new DesktopShortcutDialog(owner.ParentWindow).ShowModal(owner);
        Settings.Instance.InstallDesktopShortcut = result;
        Settings.OnChanged();
        Settings.Save();

        if (result == true) Install();
    }

    public static void Install() {
        if (Environment.ProcessPath is not { } path) {
            Console.Error.WriteLine("Failed install desktop shortcut: Current process path not available!");
            return;
        }

        #if LINUX
            // Desktop entry
            string desktopEntryContent =
                $"""
                 [Desktop Entry]
                 Type=Application
                 Exec={path}
                 Name=Celeste Studio
                 GenericName=Celeste TAS Editor
                 Keywords=Celeste;Studio
                 Categories=Utility;
                 Icon=CelesteStudio
                 Comment=A feature-rich TAS editor for Celeste.
                 """;

            const string desktopName = "CelesteStudio.desktop";
            string desktopPath;
            const string iconName = "CelesteStudio";
            string iconBasePath;

            string? data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(data)) {
                desktopPath = Path.Combine(data, "applications", desktopName);
                iconBasePath = Path.Combine(data, "icons", "hicolor");
            } else if (!string.IsNullOrEmpty(home)) {
                desktopPath = Path.Combine(home, ".local", "share", "applications", desktopName);
                iconBasePath = Path.Combine(home, ".local", "share", "icons", "hicolor");
            } else {
                Console.Error.WriteLine(
                    "Failed install desktop shortcut: Environment variables $XDG_DATA_HOME and $HOME not available!");
                return;
            }

            if (Path.GetDirectoryName(desktopPath) is { } desktopDir) {
                Directory.CreateDirectory(desktopDir);
            }

            File.WriteAllText(desktopPath, desktopEntryContent);

            // PNG icons
            foreach (string size in (ReadOnlySpan<string>)["16x16", "32x32", "48x48", "64x64", "128x128", "256x256"]) {
                string iconPath = Path.Combine(iconBasePath, size, "apps", $"{iconName}.png");
                if (Path.GetDirectoryName(iconPath) is { } iconDir) {
                    Directory.CreateDirectory(iconDir);
                }

                using var iconData = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Icon_{size}.png")!;
                using var fileWriter = File.OpenWrite(iconPath);

                iconData.CopyTo(fileWriter);
            }
            // SVG icon
            {
                string iconPath = Path.Combine(iconBasePath, "scalable", "apps", $"{iconName}.svg");
                if (Path.GetDirectoryName(iconPath) is { } iconDir) {
                    Directory.CreateDirectory(iconDir);
                }

                using var iconData = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Icon.svg")!;
                using var fileWriter = File.OpenWrite(iconPath);

                iconData.CopyTo(fileWriter);
            }

            // Let desktop environments know that desktop entries were changed
            try {
                Process.Start("update-desktop-database", Path.GetDirectoryName(desktopPath)!);
            } catch {
                // ignored
            }
        #elif WINDOWS
            // Start Menu / Desktop .lnk
            CreateWindowsShortcut(path, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Celeste Studio.lnk"));
            CreateWindowsShortcut(path, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Celeste Studio.lnk"));
        #endif

        MessageBox.Show("Successfully created desktop shortcut for Celeste Studio!");
    }
    
    private static void CreateWindowsShortcut(string exePath, string lnkPath) {
        // ReSharper disable once SuspiciousTypeConversion.Global
        var link = (IShellLink) new ShellLink();
        link.SetDescription("Launch Celeste Studio");
        link.SetPath(exePath);
        link.SetWorkingDirectory(Directory.GetParent(exePath)!.FullName);
        // ReSharper disable once SuspiciousTypeConversion.Global
        ((IPersistFile) link).Save(lnkPath, false);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
