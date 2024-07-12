using System;
using System.Collections.Generic;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Editing;

public class AutoCompleteMenu : Scrollable {
    public record Entry {
        /// The text which will be used for filtering results.
        public required string SearchText;
        /// The text which will be displayed inside the menu.
        public required string DisplayText;
        /// The extra text which will be displayed to the right of the main text.
        public required string ExtraText;
        /// Callback when this entries is selected.
        public required Action OnUse;
    }
    
    public override bool Visible {
        get => base.Visible;
        set {
            base.Visible = value;
            filter = string.Empty;
            selectedEntry = 0;
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
            labels[selectedEntry].Display.BackgroundColor = labels[selectedEntry].Extra.BackgroundColor = Colors.Transparent;
            selectedEntry = Math.Clamp(value, 0, shownEntries.Length);
            labels[selectedEntry].Display.BackgroundColor = labels[selectedEntry].Extra.BackgroundColor = Settings.Instance.Theme.AutoCompleteSelected;

            ScrollIntoView();
        }
    }
    
    private const int BorderWidth = 1; // We can't disable the border, so we have to account for that to avoid having scrollbars
    private const float EntryPadding = 2.5f;
    private const int DisplayTextPadding = 2; // A space is added on both sides of the DisplayText
    private const int ExtraTextPadding = 2; // Two spaces are added on the right side of the ExtraText
    
    public int ContentWidth {
        get {
            if (shownEntries.Length == 0) {
                return 0;
            }
            
            var font = FontManager.EditorFontRegular;
            int maxDisplayLen = shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);
            int maxExtraLen = shownEntries.Select(entry => entry.ExtraText.Length).Aggregate(Math.Max);
            
            // Need to add +2 at the end, since otherwise there's a horizontal scroll bar for some reason
            return (int)(font.CharWidth() * (maxDisplayLen + DisplayTextPadding)) + (int)(font.CharWidth() * (maxExtraLen + ExtraTextPadding)) + BorderWidth * 2;
        }
    }
    public int ContentHeight => shownEntries.Length * EntryHeight + BorderWidth * 2;
    public int EntryHeight => (int)(FontManager.EditorFontRegular.LineHeight() + EntryPadding * 2.0f);
    
    private Entry[] shownEntries = [];
    private (Label Display, Label Extra)[] labels = [];
    
    public AutoCompleteMenu() {
        Settings.ThemeChanged += Recalc;
        Recalc();
    }
    
    private void Recalc() {
        shownEntries = entries.Where(entry => entry.SearchText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
        if (shownEntries.Length == 0) {
            Visible = false;
            return;
        }
        
        selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);
        
        var table = new TableLayout {
            Padding = 0,
            Spacing = new Size(0, 0),
            BackgroundColor = Settings.Instance.Theme.AutoCompleteBg,
        };
        Array.Resize(ref labels, shownEntries.Length);
        
        // Prevent the click from going to the editor
        table.MouseDown += (_, e) => e.Handled = true;
        
        var font = FontManager.EditorFontRegular;
        int maxDisplayLen = shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);
        int maxExtraLen = shownEntries.Select(entry => entry.ExtraText.Length).Aggregate(Math.Max);
        
        for (int i = 0; i < shownEntries.Length; i++) {
            var entry = shownEntries[i];
            
            var defaultBg = SelectedEntry == i ? Settings.Instance.Theme.AutoCompleteSelected : Colors.Transparent;
            
            var display = new Label {
                Text = $" {entry.DisplayText} ",
                TextColor = Settings.Instance.Theme.AutoCompleteFg,
                BackgroundColor = defaultBg,
                Font = font,
                VerticalAlignment = VerticalAlignment.Center,
                Height = (int) (font.LineHeight() + EntryPadding * 2.0f),
                Width = (int) (font.CharWidth() * (maxDisplayLen + DisplayTextPadding)),
                Cursor = Cursors.Pointer,
            };
            var extra = new Label {
                Text = $"{entry.ExtraText}  ",
                TextColor = Settings.Instance.Theme.AutoCompleteFgExtra,
                BackgroundColor = defaultBg,
                Font = font,
                VerticalAlignment = VerticalAlignment.Center,
                Height = (int) (font.LineHeight() + EntryPadding * 2.0f),
                Width = (int) (font.CharWidth() * (maxExtraLen + ExtraTextPadding)),
                Cursor = Cursors.Pointer,
            };
            
            labels[i] = (display, extra);
            
            display.MouseDown += (_, e) => entry.OnUse();
            
            // Hover styling
            int idx = i;
            void SetHover(bool hover) {
                if (hover && SelectedEntry != idx) {
                    display.BackgroundColor = Settings.Instance.Theme.AutoCompleteHovered;
                    extra.BackgroundColor = Settings.Instance.Theme.AutoCompleteHovered;
                } else {
                    display.BackgroundColor = extra.BackgroundColor = SelectedEntry == idx ? Settings.Instance.Theme.AutoCompleteSelected : Colors.Transparent;
                }
            }
            display.MouseEnter += (_, e) => SetHover(true);
            extra.MouseEnter += (_, e) => SetHover(true);
            display.MouseLeave += (_, e) => SetHover(false);
            extra.MouseLeave += (_, e) => SetHover(false);
            
            table.Rows.Add(new TableRow {
                Cells = {
                    new TableCell {Control = display},
                    new TableCell {Control = extra},
                }
            });
        }
        
        ScrollSize = table.Size;
        Content = table;
    }
    
    private void ScrollIntoView() {
        const int lookAhead = 2;
        
        int entryHeight = EntryHeight;
        int scrollStartTop = ScrollPosition.Y + lookAhead * entryHeight;
        int scrollStartBottom = ScrollPosition.Y + ClientSize.Height - lookAhead * entryHeight;
        
        int selectedTop = SelectedEntry * entryHeight;
        int selectedBottom = selectedTop + entryHeight;
        
        Console.WriteLine($"{selectedTop} {selectedBottom} | {scrollStartTop} {scrollStartBottom}");
        if (selectedTop < scrollStartTop) {
            ScrollPosition = ScrollPosition with { Y = Math.Max(0, selectedTop - lookAhead * entryHeight) };
        } else if (selectedBottom > scrollStartBottom) {
            ScrollPosition = ScrollPosition with { Y = Math.Min(shownEntries.Length * entryHeight - ClientSize.Height, selectedBottom + lookAhead * entryHeight - ClientSize.Height) }; 
        }
    }
    
    public bool HandleKeyDown(KeyEventArgs e) {
        if (!Visible)
            return false;
        
        if (e.Key == Keys.Up) {
            SelectedEntry = (SelectedEntry - 1).Mod(shownEntries.Length);
            return true;
        }
        if (e.Key == Keys.Down) {
            SelectedEntry = (SelectedEntry + 1).Mod(shownEntries.Length);
            return true;
        }
        if (e.Key is Keys.Enter or Keys.Tab) {
            shownEntries[SelectedEntry].OnUse();
            return true;
        }
        if (e.Key == Keys.Escape) {
            Visible = false;
            return true;
        }
        
        return false;
    }
}
