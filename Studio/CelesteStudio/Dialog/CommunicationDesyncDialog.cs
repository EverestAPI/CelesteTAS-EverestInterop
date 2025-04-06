using Eto.Forms;
using StudioCommunication.Util;
using System;

namespace CelesteStudio.Dialog;

public class CommunicationDesyncDialog : Eto.Forms.Dialog {
    private CommunicationDesyncDialog(ushort studioVersion, ushort celesteTasVersion) {
        Title = "Connection failed";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            Items = {
                new Label { Text = celesteTasVersion > studioVersion
                    ? """
                      Failed to connect to CelesteTAS!
                      Your Celeste Studio version is outdated.
                      
                      Update CelesteTAS (either through Olympus or the button below) to the latest version and launch the game.
                      Celeste Studio should automatically be updated during startup.
                      
                      If that doesn't work, you can manually download both CelesteTAS and Celeste Studio from the latest release (they need to be both from the same release)
                      Note that the latter method isn't really recommended and won't work with launching Studio from Celeste. (Though it can be placed anywhere for it to work in general)
                      """
                    : """
                      Failed to connect to CelesteTAS!
                      Your CelesteTAS version is outdated.

                      Update CelesteTAS (either through Olympus or the button below) to the latest version and launch the game.
                      Celeste Studio should automatically be updated during startup.

                      If that doesn't work, you can manually download both CelesteTAS and Celeste Studio from the latest release (they need to be both from the same release)
                      Note that the latter method isn't really recommended and won't work with launching Studio from Celeste. (Though it can be placed anywhere for it to work in general)
                      """
                },
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = {
                        new Button((_, _) => {
                            var proc = ProcessHelper.OpenInDefaultApp("https://0x0a.de/twoclick/?https://github.com/EverestAPI/CelesteTAS-EverestInterop/releases/latest/download/CelesteTAS.zip");
                            proc?.WaitForExit(TimeSpan.FromSeconds(10));
                            Environment.Exit(1);
                        }) { Text = "Update CelesteTAS" },

                        new Button((_, _) => {
                            var proc = ProcessHelper.OpenInDefaultApp("https://github.com/EverestAPI/CelesteTAS-EverestInterop/releases/latest");
                            proc?.WaitForExit(TimeSpan.FromSeconds(10));
                            Environment.Exit(1);
                        }) { Text = "Download latest Release" },
                        new Button((_, _) => Environment.Exit(1)) { Text = "Exit Studio" },
                        new Button((_, _) => Close()) { Text = "Continue without Connection to CelesteTAS" },
                    }
                },
            },
        };

        Load += (_, _) => {
            // Need to make sure, the theme is applied
            Settings.OnThemeChanged();
        };
        Studio.RegisterDialog(this);
    }

    public static void Show(ushort studioVersion, ushort celesteTasVersion) {
        new CommunicationDesyncDialog(studioVersion, celesteTasVersion).ShowModal();
    }
}
