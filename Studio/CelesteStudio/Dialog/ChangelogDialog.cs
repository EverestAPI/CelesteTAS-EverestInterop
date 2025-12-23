using CelesteStudio.Controls;
using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StudioCommunication.Util;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContentPage = (Eto.Forms.Control Control, CelesteStudio.Controls.Markdown Content, Eto.Forms.ImageView? Image);

namespace CelesteStudio.Dialog;

/// Displays changes made inside the specified version range
public class ChangelogDialog : Eto.Forms.Dialog {
    private readonly record struct VersionHistory(
        Dictionary<string, string> CategoryNames,
        List<VersionEntry> Versions
    );
    private readonly record struct VersionEntry(
        Version CelesteTasVersion,
        Version StudioVersion,
        List<Page> Pages,
        Dictionary<string, List<string>> Changes
    );
    private readonly record struct Page(
        string Text,
        Image? Image
    );
    private enum Alignment { Left, Right }
    private readonly record struct Image(
        string Source,
        Alignment Align,
        int Width,
        int Height
    );

    private const int PaddingSize = 10;

    /// Require the user to spend at least 1s on each page, to discourage skipping through everything
    /// and have them at least look at the title and hopefully the body if it sounds interessting
    private const int MinPageTimeMS = 1000;
    private CancellationTokenSource? unlockPageTokenSource;
    private readonly bool forceShowPages;

    private int currentPage = 0, maxAvailablePage = 0;
    private readonly LazyValue<ContentPage>[] contentPages;

    private readonly Button nextButton;
    private readonly Button prevButton;
    private readonly Label pageLabel;
    private readonly DynamicLayout buttonsLayout;
    private readonly Scrollable scrollable;

    private StackLayoutItem contentItem;

    private ChangelogDialog(VersionHistory versionHistory, List<Page> pages, Dictionary<string, List<string>> changes, Version oldVersion, Version newVersion, bool forceShow) {
        string title = $"# CelesteTAS v{newVersion.ToString(3)}";
        Version? oldStudioVersion = null;
        foreach (var version in versionHistory.Versions) {
            if (version.CelesteTasVersion == oldVersion) {
                oldStudioVersion = version.StudioVersion;
            }

            if (version.CelesteTasVersion == newVersion) {
                if (oldStudioVersion != version.StudioVersion) {
                    // Append Studio to title
                    title += $" / Studio v{version.StudioVersion.ToString(3)}";
                }

                break;
            }
        }

        scrollable = new Scrollable { Border = BorderType.None };

        contentPages = new LazyValue<ContentPage>[pages.Count + 1];
        forceShowPages = forceShow && contentPages.Length <= 10; // Let's spare the user when having more than 10 pages

        for (int i = 0; i < pages.Count; i++) {
            int currIdx = i;
            contentPages[i] = new LazyValue<ContentPage>(() => {
                var page = pages[currIdx];

                var markdown = new Markdown($"{title}\n{pages[currIdx].Text}", scrollable) { Padding = new(left: 0, top: 0, right: Studio.ScrollBarSize + PaddingSize, bottom: 0) };

                if (Eto.Platform.Instance.IsGtk) {
                    int prevHeight = -1;
                    markdown.PostDraw += () => {
                        if (prevHeight != markdown.RequiredHeight) {
                            prevHeight = markdown.RequiredHeight;
                            ApplySize(null, EventArgs.Empty);
                        }
                    };
                }

                if (page.Image is not { } image) {
                    return (scrollable, markdown, null);
                }

                string srcPath = Path.Combine(Studio.InstallDirectory, image.Source);
                if (!File.Exists(srcPath)) {
                    return (scrollable, markdown, null);
                }

                using var fs = File.OpenRead(srcPath);
                var view = new ImageView { Image = new Bitmap(fs), Width = image.Width, Height = image.Height };
                Console.WriteLine(image.Source);

                return (pages[currIdx].Image!.Value.Align switch {
                    Alignment.Left => new StackLayout { Orientation = Orientation.Horizontal, VerticalContentAlignment = VerticalAlignment.Center, Spacing = PaddingSize, Items = { view, scrollable } },
                    Alignment.Right => new StackLayout { Orientation = Orientation.Horizontal, VerticalContentAlignment = VerticalAlignment.Center, Spacing = PaddingSize, Items = { scrollable, view } },
                    _ => throw new ArgumentOutOfRangeException()
                }, markdown, view);
            });
        }
        contentPages[^1] = new LazyValue<ContentPage>(() => {
            var builder = new StringBuilder();

            builder.AppendLine(title);
            foreach ((string category, string name) in versionHistory.CategoryNames) {
                if (!changes.TryGetValue(category, out var categoryChanges) || categoryChanges.Count == 0) {
                    continue;
                }

                builder.AppendLine($"## {name}");
                foreach (string change in categoryChanges) {
                    builder.AppendLine($"- {change}");
                }
            }

            var markdown = new Markdown(builder.ToString(), scrollable) { Padding = new(left: 0, top: 0, right: Studio.ScrollBarSize + PaddingSize, bottom: 0) };

            if (Eto.Platform.Instance.IsGtk) {
                int prevHeight = -1;
                markdown.PostDraw += () => {
                    if (prevHeight != markdown.RequiredHeight) {
                        prevHeight = markdown.RequiredHeight;
                        ApplySize(null, EventArgs.Empty);
                    }
                };
            }
            return (scrollable, markdown, null);
        });

        Resizable = true;
        Closeable = !forceShowPages;
        MinimumSize = new Size(400, 300);
        Size = new Size(900, 550);
        Title = "What's new?";

        nextButton = new Button { Text = "Next" };
        nextButton.Click += (_, _) => SwitchToPage(currentPage + 1);

        prevButton = new Button { Text = "Previous" };
        prevButton.Click += (_, _) => SwitchToPage(currentPage - 1);

        buttonsLayout = new DynamicLayout();
        buttonsLayout.BeginHorizontal();
        buttonsLayout.Add(prevButton);
        buttonsLayout.AddSpace();
        buttonsLayout.AddCentered(pageLabel = new Label());
        buttonsLayout.AddSpace();
        buttonsLayout.Add(nextButton);

        SizeChanged += ApplySize;
        Shown += ApplySize;

        Content = new StackLayout {
            Padding = PaddingSize,
            Spacing = PaddingSize,
            Items = {
                (contentItem = new StackLayoutItem {
                    HorizontalAlignment = HorizontalAlignment.Center
                }),
                buttonsLayout,
            }
        };

        SwitchToPage(0);

        Load += (_, _) => {
            // Need to make sure, the theme is applied
            Settings.OnThemeChanged();
        };
        Studio.RegisterDialog(this);
    }

    private void SwitchToPage(int page) {
        if (page < 0) {
            return;
        }
        if (page >= contentPages.Length) {
            Close();
            return;
        }

        currentPage = page;
        if (contentPages.Length > 1) {
            prevButton.Enabled = page > 0;
            pageLabel.Text = $"Page {page + 1} / {contentPages.Length}";
        } else {
            prevButton.Visible = false;
            pageLabel.Visible = false;
        }

        if (page < contentPages.Length - 1) {
            nextButton.Text = "Next";
        } else {
            nextButton.Text = "Close";
        }

        if (forceShowPages) {
            unlockPageTokenSource?.Cancel();
            unlockPageTokenSource?.Dispose();
            if (currentPage == maxAvailablePage) {
                unlockPageTokenSource = new CancellationTokenSource();
                nextButton.Enabled = false;

                var token = unlockPageTokenSource.Token;
                Task.Run(async () => {
                    await Task.Delay(MinPageTimeMS, token);
                    if (token.IsCancellationRequested) {
                        return;
                    }

                    maxAvailablePage = Math.Max(maxAvailablePage, currentPage + 1);
                    await Application.Instance.InvokeAsync(() => {
                        nextButton.Enabled = true;
                        Closeable = maxAvailablePage == contentPages.Length;
                    });
                }, token);
            } else {
                unlockPageTokenSource = null;
                nextButton.Enabled = true;
            }
        }

        bool firstLoad = scrollable.Content == null;
        scrollable.Content = null;
        contentItem.Control = null;

        contentPages[page].Reset(); // Force regeneration, since otherwise the Markdown is apparently just empty?
        var (control, content, _) = contentPages[page].Value;
        scrollable.Content = content;

        var stackLayout = (StackLayout)Content;
        stackLayout.Items.Clear();
        stackLayout.Items.Add(contentItem = new StackLayoutItem {
            Control = control,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stackLayout.Items.Add(buttonsLayout);

        // Applying the proper size for the first page is already handled in the constructor
        if (!firstLoad) {
            UpdateLayout();
            ApplySize(null, EventArgs.Empty);
        }
    }
    void ApplySize(object? _1, EventArgs _2) {
        var (_, content, image) = contentPages[currentPage].Value;

        // Gotta love the scrollable experience
        if (Eto.Platform.Instance.IsWpf) {
            scrollable.ScrollSize = new Size(
                Math.Max(0, ClientSize.Width - Studio.ScrollBarSize - (image?.Width ?? PaddingSize) - PaddingSize*3),
                Math.Max(ClientSize.Height - buttonsLayout.Height - PaddingSize*3, content.RequiredHeight)
            );
        } else {
            content.UpdateLayout();
            content.Width = Math.Max(0, ClientSize.Width - Studio.ScrollBarSize - (image?.Width ?? PaddingSize) - PaddingSize*3);
            if (content.RequiredHeight != 0) {
                content.Height = Math.Max(ClientSize.Height - buttonsLayout.Height - PaddingSize*3, content.RequiredHeight);
            }
        }

        scrollable.Width = Math.Max(0, ClientSize.Width - (image?.Width ?? PaddingSize) - PaddingSize*3);
        scrollable.Height = contentItem.Control.Height = Math.Max(0, ClientSize.Height - buttonsLayout.Height - PaddingSize*3);
        buttonsLayout.Width = Math.Max(0, ClientSize.Width - PaddingSize*2);
    }

    private class VersionConverter : JsonConverter<Version> {
        public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            return Version.TryParse(reader.GetString(), out var version) ? version : null;
        }
        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options) {
            writer.WriteStringValue(value.ToString(3));
        }
    }

    public static void Show(FileStream versionHistoryFile, Version? oldVersion, Version? newVersion, bool forceShow) {
        var versionHistory = JsonSerializer.Deserialize<VersionHistory>(versionHistoryFile, new JsonSerializerOptions {
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new VersionConverter(),
            },
        });

        if (versionHistory.Versions.Count == 0) {
            return;
        }

        newVersion ??= versionHistory.Versions[0].CelesteTasVersion;
        oldVersion ??= versionHistory.Versions.Count >= 2 ? versionHistory.Versions[1].CelesteTasVersion : new Version(0, 0, 0);

        // Collect pages and changes
        List<Page> pages = [];
        Dictionary<string, List<string>> changes = [];

        foreach (var version in versionHistory.Versions) {
            // Either limit to range or just current version
            if (version.CelesteTasVersion <= oldVersion || version.CelesteTasVersion > newVersion) {
                continue;
            }

            pages.AddRange(version.Pages);

            foreach (string category in version.Changes.Keys) {
                changes.AddRangeToKey(category, version.Changes[category]);
            }
        }

        new ChangelogDialog(versionHistory, pages, changes, oldVersion, newVersion, forceShow).ShowModal();
    }
}
