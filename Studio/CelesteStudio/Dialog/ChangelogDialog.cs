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
    private const int PagerHeight = 32;

    private int currentPage = 0;
    private readonly LazyValue<ContentPage>[] contentPages;

    private readonly Button nextButton;
    private readonly Button prevButton;
    private readonly Label pageLabel;
    private readonly DynamicLayout buttonsLayout;
    private readonly Scrollable scrollable;

    // Honestly.. idk why those scalars are like that.. they kinda just work..
    private int ContentWidth => Eto.Platform.Instance.IsGtk
        ? Math.Max(0, Width - PaddingSize * 3)
        : Eto.Platform.Instance.IsMac
            ? Math.Max(0, Width - PaddingSize * 2)
            : Math.Max(0, Width - PaddingSize * 4);
    private int ContentHeight => Eto.Platform.Instance.IsGtk
        ? Math.Max(0, Height - PagerHeight * 2 - PaddingSize * 5)
        : Eto.Platform.Instance.IsMac
            ? Math.Max(0, Height - PagerHeight * 2 - PaddingSize * 3)
            : Math.Max(0, Height - PagerHeight * 2 - PaddingSize * 4);

    private ChangelogDialog(VersionHistory versionHistory, List<Page> pages, Dictionary<string, List<string>> changes, Version oldVersion, Version newVersion) {
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

        contentPages = new LazyValue<ContentPage>[pages.Count + 1];
        for (int i = 0; i < pages.Count; i++) {
            int currIdx = i;
            contentPages[i] = new LazyValue<ContentPage>(() => {
                var page = pages[currIdx];

                var markdown = new Markdown($"{title}\n{pages[currIdx].Text}", scrollable);

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
                    return (markdown, markdown, null);
                }

                string srcPath = Path.Combine(Studio.InstallDirectory, image.Source);
                if (!File.Exists(srcPath)) {
                    return (markdown, markdown, null);
                }

                using var fs = File.OpenRead(srcPath);
                var view = new ImageView { Image = new Bitmap(fs), Width = image.Width, Height = image.Height };

                return (pages[currIdx].Image!.Value.Align switch {
                    Alignment.Left => new StackLayout { Orientation = Orientation.Horizontal, VerticalContentAlignment = VerticalAlignment.Center, Spacing = PaddingSize, Items = { view, markdown } },
                    Alignment.Right => new StackLayout { Orientation = Orientation.Horizontal, VerticalContentAlignment = VerticalAlignment.Center, Spacing = PaddingSize, Items = { markdown, view } },
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

            var markdown = new Markdown(builder.ToString(), scrollable);

            if (Eto.Platform.Instance.IsGtk) {
                int prevHeight = -1;
                markdown.PostDraw += () => {
                    if (prevHeight != markdown.RequiredHeight) {
                        prevHeight = markdown.RequiredHeight;
                        ApplySize(null, EventArgs.Empty);
                    }
                };
            }
            return (markdown, markdown, null);
        });

        Resizable = true;
        MinimumSize = new Size(400, 300);
        Size = new Size(800, 600);
        Title = "What's new?";

        nextButton = new Button { Text = "Next" };
        nextButton.Click += (_, _) => SwitchToPage(currentPage + 1);

        prevButton = new Button { Text = "Previous" };
        prevButton.Click += (_, _) => SwitchToPage(currentPage - 1);

        buttonsLayout = new DynamicLayout() { Height = PagerHeight };
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
                new StackLayoutItem {
                    Control = (scrollable = new Scrollable {
                        Border = BorderType.None
                    }),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
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

        bool firstLoad = scrollable.Content == null;
        scrollable.Content = null;
        scrollable.Content = contentPages[page].Value.Control;

        // Applying the proper size for the first page is already handle in the constructor
        if (!firstLoad) {
            UpdateLayout();
            ApplySize(null, EventArgs.Empty);
        }
    }
    void ApplySize(object? _1, EventArgs _2) {
        int width = ContentWidth, height = ContentHeight;

        var (_, content, image) = contentPages[currentPage].Value;

        // Gotta love the scrollable experience
        if (Eto.Platform.Instance.IsWpf) {
            scrollable.ScrollSize = new Size(
                Math.Max(0, width - (image?.Width ?? 0) - 20),
                Math.Max(height, content.RequiredHeight)
            );
        } else {
            content.UpdateLayout();
            content.Width = Math.Max(0, width - (image?.Width ?? 0) - 20);
            if (content.RequiredHeight != 0) {
                content.Height = Math.Max(height, content.RequiredHeight);
            }
        }

        scrollable.Height = height;
        buttonsLayout.Width = width;
    }

    private class VersionConverter : JsonConverter<Version> {
        public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            return Version.TryParse(reader.GetString(), out var version) ? version : null;
        }
        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options) {
            writer.WriteStringValue(value.ToString(3));
        }
    }

    public static void Show(FileStream versionHistoryFile, Version? oldVersion, Version? newVersion) {
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

        new ChangelogDialog(versionHistory, pages, changes, oldVersion, newVersion).ShowModal();
    }
}
