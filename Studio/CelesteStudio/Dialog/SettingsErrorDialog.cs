using Eto;
using Eto.Forms;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;

namespace CelesteStudio.Dialog;

public enum SettingsErrorAction { TryAgain, Reset, Edit, Exit, None }

public class SettingsErrorDialog : Dialog<SettingsErrorAction> {
    private SettingsErrorDialog(Exception ex) {
        Title = "Settings failed to load";
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            Items = {
                new Label { Text =
                    """
                    An error occurred while trying the load the settings!
                    This is either caused by a corrupted settings file, or a bug in Studio.
                    Please consider reporting this, with the error below attached.
                    """ },
                new StackLayout {
                    Spacing = 10,
                    Items = {
                        new TextArea {
                            Text = ex.ToString(),
                            ReadOnly = true,
                            Width = 500,
                            Height = 200,
                        },
                        new Button((_, _) => {
                            Clipboard.Instance.Clear();
                            Clipboard.Instance.Text =
                                // Add some additional info to the report
                                $"""
                                Celeste Studio {Studio.Version}
                                Platform: {Platform.Instance}
                                {DateTime.Now.ToString(CultureInfo.InvariantCulture)}

                                Stacktrace:
                                ```
                                {ex}
                                ```
                                Settings File:
                                ```
                                {File.ReadAllText(Settings.SettingsPath)}
                                ```
                                """
                                // Prevent usernames from appearing inside reports
                                .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
                        }) { Text = "Copy error report to clipboard" }
                    }
                },
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = {
                        new Button((_, _) => Close(SettingsErrorAction.TryAgain)) { Text = "Try again" },
                        new Button((_, _) => Close(SettingsErrorAction.Reset)) { Text = "Reset settings to default" },
                        new Button((_, _) => Close(SettingsErrorAction.Edit)) { Text = "Manually edit settings file" },
                        new Button((_, _) => Close(SettingsErrorAction.Exit)) { Text = "Exit Studio" },
                    }
                },
            },
        };

        Result = SettingsErrorAction.None;

        Load += (_, _) => {
            // Need to make sure, the theme is applied
            Settings.OnThemeChanged();
        };
        Studio.RegisterDialog(this);
    }

    protected override void OnClosing(CancelEventArgs e) {
        if (Result == SettingsErrorAction.None) {
            MessageBox.Show("Please choose one of the four actions!");
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);

    }

    public static SettingsErrorAction Show(Exception ex) {
        return new SettingsErrorDialog(ex).ShowModal();
    }
}
