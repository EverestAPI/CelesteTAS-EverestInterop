using Eto.Forms;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

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

    public static void Install() {
        if (Environment.ProcessPath is not { } path) {
            Console.Error.WriteLine("Failed install desktop shortcut: Current process path not available!");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
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

            MessageBox.Show("Successfully created desktop shortcut for Celeste Studio!");
        }
    }

    public static void Show(Control owner) {
        bool? result = new DesktopShortcutDialog(owner.ParentWindow).ShowModal(owner);
        Settings.Instance.InstallDesktopShortcut = result;
        Settings.OnChanged();
        Settings.Save();

        if (result == true) Install();
    }
}
