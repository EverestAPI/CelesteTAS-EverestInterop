using Eto.Drawing;
using Eto.Forms;
using LibGit2Sharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CelesteStudio.Dialog.Git;

public class CloneRepositoryDialog : Eto.Forms.Dialog {
    private readonly TextBox urlBox;
    private readonly TextBox targetDirBox;
    private bool modifiedTargetDir;

    private CloneRepositoryDialog() {
        const int width = 400;

        urlBox = new TextBox { PlaceholderText = "https://github.com/VampireFlower/CelesteTAS.git", Width = width };
        targetDirBox = new TextBox { Width = width };

        urlBox.TextChanged += (_, _) => {
            if (CreateTargetPath() is { } target) {
                targetDirBox.Text = target;
            }
        };
        targetDirBox.TextChanged += (_, _) => modifiedTargetDir = targetDirBox.Text != CreateTargetPath();

        var layout = new DynamicLayout { Padding = 10, DefaultSpacing = new Size(10, 10) };
        layout.BeginVertical();
        layout.BeginHorizontal();

        layout.AddCentered(new Label { Text = "Repository URL" });
        layout.Add(urlBox);

        layout.EndBeginHorizontal();

        layout.AddCentered(new Label { Text = "Target Directory" });
        layout.Add(targetDirBox);

        layout.EndHorizontal();
        layout.EndVertical();

        Title = "Clone Git Repository";
        Content = layout;

        DefaultButton = new Button((_, _) => Clone()) { Text = "&Clone", Enabled = false };
        AbortButton = new Button((_, _) => Close()) { Text = "&Cancel" };

        urlBox.TextChanged += (_, _) => DefaultButton.Enabled = Validate();
        targetDirBox.TextChanged += (_, _) => DefaultButton.Enabled = Validate();

        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);

        Studio.RegisterDialog(this);
    }

    private Uri? TryCreateUri() {
        if (string.IsNullOrWhiteSpace(urlBox.Text)) {
            return null;
        }

        try {
            var builder = new UriBuilder(urlBox.Text) { Scheme = Uri.UriSchemeHttps, Port = -1 };
            return builder.Uri;
        } catch (UriFormatException) {
            return null;
        }
    }
    private string? CreateTargetPath() {
        return !modifiedTargetDir && TryCreateUri() is { } uri && uri.Segments.Length > 1
            ? Path.Combine(Studio.Instance.GetCurrentBaseDirectory(), uri.Segments[^1])
            : null;
    }
    private bool Validate() {
        return !string.IsNullOrWhiteSpace(targetDirBox.Text) && TryCreateUri() != null && !Directory.Exists(targetDirBox.Text);
    }

    private void Clone() {
        if (TryCreateUri() is not { } uri) {
            MessageBox.Show("Provided invalid repository URL!", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }
        if (Directory.Exists(targetDirBox.Text)) {
            MessageBox.Show("Target directory already exists!", MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }

        string targetDir = Path.GetFullPath(targetDirBox.Text);
        if (Path.GetDirectoryName(targetDir) is { } parentDir && !Directory.Exists(parentDir)) {
            Directory.CreateDirectory(parentDir);
        }

        Label progressLabel;
        ProgressBar progressBar;
        Button doneButton;

        var progressPopup = new Eto.Forms.Dialog {
            Title = "Processing...",
            Icon = Assets.AppIcon,

            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = {
                    (progressLabel = new Label { Text = "Cloning files..." }),
                    (progressBar = new ProgressBar { Width = 300 }),
                    (doneButton = new Button { Text = "Done", Enabled = false }),
                },
            },

            Resizable = false,
            Closeable = false,
            ShowInTaskbar = true,
        };
        doneButton.Click += (_, _) => {
            progressPopup.Close();
            Close();

            // Immediate allow opening a file
            Studio.Instance.OnOpenFile(targetDir);
        };

        Studio.RegisterDialog(progressPopup);
        progressPopup.Load += (_, _) => Studio.Instance.WindowCreationCallback(progressPopup);
        progressPopup.Shown += (_, _) => progressPopup.Location = Location + new Point((Width - progressPopup.Width) / 2, (Height - progressPopup.Height) / 2);

        Task.Run(() => {
            try {
                Repository.Clone(uri.AbsoluteUri, targetDir, new CloneOptions {
                    Checkout = true,
                    RecurseSubmodules = true,
                    OnCheckoutProgress = (path, completed, total) => Application.Instance.Invoke(() => UpdateProgress(path, completed, total))
                });
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                Application.Instance.Invoke(() => MessageBox.Show($"Failed to clone the Git Repository:\n{ex.Message}", MessageBoxButtons.OK, MessageBoxType.Error));
            }
        });

        progressPopup.ShowModal();
        return;

        void UpdateProgress(string current, int completed, int total) {
            progressLabel.Text = completed == total
                ? $"Successfully cloned {total} files."
                : $"Cloning files '{current}' {completed} / {total}...";
            progressBar.Value = completed;
            progressBar.MaxValue = total;

            if (completed == total) {
                doneButton.Enabled = true;
                progressPopup.Title = "Complete";
            }
        }
    }

    public static void Show() => new CloneRepositoryDialog().ShowModal();
}
