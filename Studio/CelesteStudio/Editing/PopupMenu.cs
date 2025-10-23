using CelesteStudio.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using StudioCommunication.Util;
using System.IO;
using System.Runtime.InteropServices;
using Tomlet;
using Tomlet.Exceptions;
using Tomlet.Models;

namespace CelesteStudio.Editing;

public abstract class PopupMenu : Scrollable {
    public enum Category {
        /// Manually saved as a favorite, by the user
        Favourite,
        /// Frequently used, based on personal usage statistics
        FrequentlyUsed,
        /// Suggested by the game, depending on the current situation
        Suggested,
        /// Regular entry with no special meaning
        Regular
    }
    public record Entry {
        /// The text which will be used for filtering results.
        public required string SearchText;
        /// The text which will be displayed inside the menu.
        public required string DisplayText;
        /// The extra text which will be displayed to the right of the main text.
        public required string ExtraText;

        /// Whether this entry should be suggested as "probably relevant"
        public bool Suggestion = false;

        /// Callback for when this entry is selected.
        public required Action OnUse;
        /// Whether the entry can be selected.
        public bool Disabled = false;
    }

    /// Spacing between the longest DisplayText and ExtraText of entries in characters
    private const int DisplayExtraPadding = 2;

    private sealed class ContentDrawable : SkiaDrawable {
        private readonly PopupMenu menu;

        // Specify a minimum-travel-distance to avoid very small mouse movements updating the selection
        private const float MinMouseTravelDistance = 5.0f;
        private PointF lastMouseSelection;

        public ContentDrawable(PopupMenu menu) {
            this.menu = menu;

            BackgroundColor = Colors.Transparent; // Draw background ourselves to apply rounded corners

            MouseEnter += (_, _) => Invalidate();
            MouseLeave += (_, _) => Invalidate();
            menu.Scroll += (_, _) => Invalidate();
        }

        public override int DrawX => menu.ScrollPosition.X;
        public override int DrawY => menu.ScrollPosition.Y;
        public override int DrawWidth => menu.Width;
        public override int DrawHeight => menu.Height;

        public override void Draw(SKSurface surface) {
            var canvas = surface.Canvas;

            if (menu.shownEntries.Length == 0) {
                return;
            }

            var backgroundRect = new SKRect(menu.ScrollPosition.X, menu.ScrollPosition.Y, menu.ScrollPosition.X + menu.Width, menu.ScrollPosition.Y + menu.Height);
            canvas.ClipRoundRect(new SKRoundRect(backgroundRect, Settings.Instance.Theme.PopupMenuBorderRounding), antialias: true);
            canvas.DrawRect(backgroundRect, Settings.Instance.Theme.PopupMenuBgPaint);

            var font = FontManager.SKPopupFont;
            int maxDisplayLen = menu.shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);

            float width = menu.ContentWidth - Settings.Instance.Theme.PopupMenuBorderPadding * 2.0f;
            float height = menu.EntryHeight;

            const int rowCullOverhead = 3;
            int minRow = Math.Max(0, (int)(menu.ScrollPosition.Y / height) - rowCullOverhead);
            int maxRow = Math.Min(menu.shownEntries.Length - 1, (int)((menu.ScrollPosition.Y + menu.ClientSize.Height) / height) + rowCullOverhead);

            for (int row = minRow; row <= maxRow; row++) {
                var entry = menu.shownEntries[row];

                // Highlight selected entry
                if (row == menu.SelectedEntry && !entry.Disabled) {
                    canvas.DrawRoundRect(
                        x: Settings.Instance.Theme.PopupMenuBorderPadding,
                        y: row * height + Settings.Instance.Theme.PopupMenuBorderPadding + Settings.Instance.Theme.PopupMenuEntrySpacing / 2.0f,
                        w: width,
                        h: height - Settings.Instance.Theme.PopupMenuEntrySpacing,
                        rx: Settings.Instance.Theme.PopupMenuEntryRounding,
                        ry: Settings.Instance.Theme.PopupMenuEntryRounding,
                        Settings.Instance.Theme.PopupMenuSelectedPaint);
                }

                canvas.DrawText(entry.DisplayText,
                    x: Settings.Instance.Theme.PopupMenuBorderPadding + Settings.Instance.Theme.PopupMenuEntryHorizontalPadding,
                    y: Settings.Instance.Theme.PopupMenuBorderPadding + row * height + Settings.Instance.Theme.PopupMenuEntryVerticalPadding + Settings.Instance.Theme.PopupMenuEntrySpacing / 2.0f + font.Offset(),
                    font, entry.Disabled ? Settings.Instance.Theme.PopupMenuFgDisabledPaint : Settings.Instance.Theme.PopupMenuFgPaint);
                canvas.DrawText(entry.ExtraText,
                    x: Settings.Instance.Theme.PopupMenuBorderPadding + Settings.Instance.Theme.PopupMenuEntryHorizontalPadding + font.CharWidth() * (maxDisplayLen + DisplayExtraPadding),
                    y: Settings.Instance.Theme.PopupMenuBorderPadding + row * height + Settings.Instance.Theme.PopupMenuEntryVerticalPadding + Settings.Instance.Theme.PopupMenuEntrySpacing / 2.0f + font.Offset(),
                    font, Settings.Instance.Theme.PopupMenuFgExtraPaint);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            int mouseRow = (int)((e.Location.Y - Settings.Instance.Theme.PopupMenuBorderPadding) / menu.EntryHeight);
            if (mouseRow >= 0 && mouseRow < menu.shownEntries.Length && !menu.shownEntries[mouseRow].Disabled) {
                Cursor = Cursors.Pointer;

                // Only update selection if mouse has actually moved
                if ((Mouse.Position - lastMouseSelection).LengthSquared >= MinMouseTravelDistance * MinMouseTravelDistance) {
                    menu.SelectedEntry = mouseRow;
                    lastMouseSelection = Mouse.Position;
                }
            } else {
                Cursor = null;
            }

            e.Handled = true;
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            e.Handled = true;

            if (e.Buttons.HasFlag(MouseButtons.Primary)) {
                int mouseRow = (int)(e.Location.Y / menu.EntryHeight);
                if (mouseRow >= 0 && mouseRow < menu.shownEntries.Length && menu.shownEntries[mouseRow] is var currEntry && !currEntry.Disabled) {
                    if (menu.Data is { } data) {
                        ref uint amount = ref CollectionsMarshal.GetValueRefOrAddDefault(data.Usages, currEntry.SearchText, out bool _);
                        amount++;
                        SaveStorage();
                    }

                    currEntry.OnUse();
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            // Update selected entry on scroll
            menu.Scroll += OnScroll;

            if (Settings.Instance.ScrollSpeed > 0.0f) {
                // Manually scroll to respect our scroll speed
                menu.ScrollPosition = menu.ScrollPosition with {
                    Y = Math.Clamp((int)(menu.ScrollPosition.Y - e.Delta.Height * menu.EntryHeight * Settings.Instance.ScrollSpeed), 0, Height - menu.ClientSize.Height)
                };
                e.Handled = true;
            }

            base.OnMouseWheel(e);

            return;

            void OnScroll(object? _1, EventArgs _2) {
                int mouseRow = (int)((PointFromScreen(Mouse.Position).Y - Settings.Instance.Theme.PopupMenuBorderPadding) / menu.EntryHeight);
                if (mouseRow >= 0 && mouseRow < menu.shownEntries.Length && !menu.shownEntries[mouseRow].Disabled) {
                    menu.SelectedEntry = mouseRow;
                    lastMouseSelection = Mouse.Position;
                }

                menu.Scroll -= OnScroll;
            }
        }
    }

    #region Storage

    private record StorageData(HashSet<string> Favourites, Dictionary<string, uint> Usages);
    private static readonly Dictionary<string, StorageData> popupDataStorage = new();

    /// Unique identifier, used for storing favourites and usage statistics
    public string? StorageKey;

    /// Active data slot, used for storing persistant data
    private StorageData? Data {
        get {
            if (string.IsNullOrEmpty(StorageKey)) {
                return null; // Data collection is disabled for this popup
            }

            ref var data = ref CollectionsMarshal.GetValueRefOrAddDefault(popupDataStorage, StorageKey, out bool exists);
            if (!exists) {
                return data = new StorageData(Favourites: new(), Usages: new());
            }

            return data!;
        }
    }

    public static void LoadStorage() {
        popupDataStorage.Clear();

        if (!File.Exists(Settings.PopupStoragePath)) {
            return;
        }

        var toml = TomlParser.ParseFile(Settings.PopupStoragePath);

        foreach ((string key, var tomlValue) in toml.Entries) {
            if (tomlValue is not TomlTable table) {
                throw new TomlTypeMismatchException(typeof(TomlTable), tomlValue.GetType(), typeof(StorageData));
            }

            popupDataStorage[key] = new StorageData(
                table.GetArray(nameof(StorageData.Favourites)).ArrayValues
                    .Select(value => value.StringValue)
                    .ToHashSet(),
                table.GetArray(nameof(StorageData.Usages)).ArrayValues
                    .Select(e => {
                        if (e is not TomlTable entry) {
                            throw new TomlTypeMismatchException(typeof(TomlTable), e.GetType(), typeof((string, uint)));
                        }
                        if (entry.GetValue("Name") is not TomlString name) {
                            throw new TomlTypeMismatchException(typeof(TomlString), table.GetValue("Name").GetType(), typeof(string));
                        }
                        if (entry.GetValue("Amount") is not TomlLong amount || amount.Value < 0) {
                            throw new TomlTypeMismatchException(typeof(TomlLong), table.GetValue("Amount").GetType(), typeof(uint));
                        }

                        return (Name: name.StringValue, Amount: (uint) amount.Value);
                    })
                    .ToDictionary(e => e.Name, e => e.Amount)
            );
        }
    }
    public static void SaveStorage() {
        var toml = TomlDocument.CreateEmpty();

        foreach ((string key, var data) in popupDataStorage) {
            var table = new TomlTable();

            var favourites = new TomlArray();
            foreach (string favourite in data.Favourites) {
                favourites.ArrayValues.Add(new TomlString(favourite));
            }
            var usages = new TomlArray();
            foreach ((string name, uint amount) in data.Usages) {
                var entry = new TomlTable();
                entry.Put("Name", name);
                entry.Put("Amount", amount);
                usages.ArrayValues.Add(entry);
            }

            table.Put(nameof(data.Favourites), favourites);
            table.Put(nameof(data.Usages), usages);

            // table.Put(nameof(data.Favourites), data.Favourites, quote: true);
            // table.Put(nameof(data.Usages), data.Usages.Select(e => (e.Key, Amount: e.Value)), quote: true);



            toml.PutValue(key, table);
        }

        // Write to another file and then move that over, to avoid getting interrupted while writing and corrupting the settings
        string tmpFile = Settings.PopupStoragePath + ".tmp";
        File.WriteAllText(tmpFile, toml.SerializedValue);
        File.Move(tmpFile, Settings.PopupStoragePath, overwrite: true);
    }

    #endregion

    public override bool Visible {
        get => base.Visible;
        set {
            base.Visible = value;
            filter = string.Empty;
            selectedEntry = 0;
            StorageKey = null;
        }
    }

    private string filter = string.Empty;
    public string Filter {
        get => filter;
        set {
            filter = value;
            Recalc();
        }
    }

    private List<Entry> entries = [];
    public List<Entry> Entries {
        get => entries;
        set {
            entries = value;
            Recalc();
        }
    }

    private int selectedEntry;
    public int SelectedEntry {
        get => selectedEntry;
        set {
            selectedEntry = Math.Clamp(value, 0, shownEntries.Length);
            drawable.Invalidate();
        }
    }

    // We update our size, but reading it won't give the updated value immediately.
    // However, we need the updated size to properly calculate other things.
    private Size actualSize = new(0, 0);

    public bool VScrollBarVisible => actualSize.Height < contentHeight && actualSize.Height > 0;
    public bool HScrollBarVisible => actualSize.Width < contentWidth && actualSize.Width > 0;

    private int contentWidth;
    public int ContentWidth {
        set => Width = actualSize.Width = Math.Max(0, value + (VScrollBarVisible ? Studio.ScrollBarSize : 0));
        get => contentWidth;
    }
    private int contentHeight;
    public int ContentHeight {
        set => Height = actualSize.Height = Math.Max(0, value + (HScrollBarVisible ? Studio.ScrollBarSize : 0));
        get => contentHeight;
    }

    public int EntryHeight => (int)(FontManager.SKPopupFont.LineHeight() + Settings.Instance.Theme.PopupMenuEntryVerticalPadding * 2.0f + Settings.Instance.Theme.PopupMenuEntrySpacing);

    private Entry[] shownEntries = [];
    private readonly ContentDrawable drawable;

    public PopupMenu() {
        LoadStorage();

        drawable = new ContentDrawable(this);
        Content = drawable;
        Border = BorderType.None;

        Recalc();
    }

    public void Recalc() {
        // Auto-close if there aren't any entries
        if (entries.Count == 0) {
            contentWidth = 0;
            contentHeight = 0;
            Visible = false;
            return;
        }

        shownEntries = entries.Where(entry => string.IsNullOrEmpty(entry.SearchText) || entry.SearchText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
        if (shownEntries.Length == 0) {
            shownEntries = [new Entry {
                DisplayText = "No results",
                SearchText = string.Empty,
                ExtraText = string.Empty,
                OnUse = null!,
                Disabled = true
            }];
        }

        selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);

        // Calculate content bounds. Calculate height first to account for scroll bar
        contentHeight = shownEntries.Length * EntryHeight + Settings.Instance.Theme.PopupMenuBorderPadding * 2;

        var font = FontManager.SKPopupFont;
        int maxDisplayLen = shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);
        int maxExtraLen = shownEntries.Select(entry => entry.ExtraText.Length).Aggregate(Math.Max);
        if (maxExtraLen != 0) {
            maxDisplayLen += DisplayExtraPadding;
        }

        contentWidth = (int)(font.CharWidth() * (maxDisplayLen + maxExtraLen) + Settings.Instance.Theme.PopupMenuEntryHorizontalPadding * 2.0f + Settings.Instance.Theme.PopupMenuBorderPadding * 2);

        drawable.Size = new(ContentWidth, ContentHeight);
        drawable.Invalidate();
    }

    private void ScrollIntoView() {
        const int lookAhead = 2;

        int entryHeight = EntryHeight;
        int scrollStartTop = ScrollPosition.Y + lookAhead * entryHeight - Settings.Instance.Theme.PopupMenuBorderPadding;
        int scrollStartBottom = ScrollPosition.Y + ClientSize.Height - lookAhead * entryHeight + Settings.Instance.Theme.PopupMenuBorderPadding;

        int selectedTop = SelectedEntry * entryHeight - Settings.Instance.Theme.PopupMenuBorderPadding;
        int selectedBottom = selectedTop + entryHeight + Settings.Instance.Theme.PopupMenuBorderPadding;

        if (selectedTop < scrollStartTop) {
            ScrollPosition = ScrollPosition with { Y = Math.Max(0, selectedTop - lookAhead * entryHeight + Settings.Instance.Theme.PopupMenuBorderPadding) };
        } else if (selectedBottom > scrollStartBottom) {
            ScrollPosition = ScrollPosition with { Y = Math.Min(shownEntries.Length * entryHeight - ClientSize.Height + Settings.Instance.Theme.PopupMenuBorderPadding * 2, selectedBottom + lookAhead * entryHeight + Settings.Instance.Theme.PopupMenuBorderPadding * 2 - ClientSize.Height) };
        }
    }

    private void MoveSelection(int direction) {
        int nextSelection = SelectedEntry;
        do {
            nextSelection = (nextSelection + Math.Sign(direction)).Mod(shownEntries.Length);
            if (nextSelection == SelectedEntry) {
                // <= 1 entries are enabled, abort movement
                return;
            }
        } while (shownEntries[nextSelection].Disabled);

        // Found a non-disabled entry
        SelectedEntry = nextSelection;
        ScrollIntoView();
    }

    public abstract bool HandleKeyDown(KeyEventArgs e);
    protected bool HandleKeyDown(KeyEventArgs e, bool useTabComplete) {
        if (!Visible) {
            return false;
        }

        if (e.Key == Keys.Escape) {
            Visible = false;
            return true;
        }

        // Don't consume inputs if nothing is interactable
        if (shownEntries.All(entry => entry.Disabled)) {
            return false;
        }

        if (e.Key == Keys.Up) {
            MoveSelection(-1);
            return true;
        }
        if (e.Key == Keys.Down) {
            MoveSelection(1);
            return true;
        }
        if (e.Key == Keys.Enter || useTabComplete && e.Key == Keys.Tab) {
            var currEntry = shownEntries[SelectedEntry];
            if (!currEntry.Disabled) {
                if (Data is { } data) {
                    ref uint amount = ref CollectionsMarshal.GetValueRefOrAddDefault(data.Usages, currEntry.SearchText, out bool _);
                    amount++;
                    SaveStorage();
                }

                currEntry.OnUse();
            }
            return true;
        }

        return false;
    }
}
