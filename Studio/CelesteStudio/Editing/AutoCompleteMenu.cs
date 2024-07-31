using System;
using System.Collections.Generic;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Editing;

public sealed class AutoCompleteMenu : Scrollable {
    public record Entry {
        /// The text which will be used for filtering results.
        public required string SearchText;
        /// The text which will be displayed inside the menu.
        public required string DisplayText;
        /// The extra text which will be displayed to the right of the main text.
        public required string ExtraText;
        /// Callback for when this entry is selected.
        public required Action OnUse;
    }
    
    private const int BorderWidth = 1; // We can't disable the border, so we have to account for that to avoid having scrollbars
    private const float EntryBetweenPadding = 2.5f;
    private const float EntryPaddingLeft = 3.0f;
    private const float EntryPaddingRight = 20.0f;
    private const int DisplayExtraPadding = 1;
    
    private sealed class ContentDrawable : Drawable {
        private readonly AutoCompleteMenu menu;
        
        public ContentDrawable(AutoCompleteMenu menu) {
            this.menu = menu;
            
            BackgroundColor = Settings.Instance.Theme.AutoCompleteBg;
            Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.AutoCompleteBg;
            
            MouseMove += (_, _) => Invalidate();
            MouseEnter += (_, _) => Invalidate();
            MouseLeave += (_, _) => Invalidate();
            menu.Scroll += (_, _) => Invalidate();
            
            Cursor = Cursors.Pointer;
        }
        
        protected override void OnPaint(PaintEventArgs e) {
            if (menu.shownEntries.Length == 0) {
                return;
            }

            var font = FontManager.EditorFontRegular;
            int maxDisplayLen = menu.shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);

            float width = menu.ContentWidth;
            float height = menu.EntryHeight;
            
            const int rowCullOverhead = 3;
            int minRow = Math.Max(0, (int)(menu.ScrollPosition.Y / height) - rowCullOverhead);
            int maxRow = Math.Min(menu.shownEntries.Length - 1, (int)((menu.ScrollPosition.Y + menu.ClientSize.Height) / height) + rowCullOverhead);
            
            using var displayBrush = new SolidBrush(Settings.Instance.Theme.AutoCompleteFg);
            using var extraBrush = new SolidBrush(Settings.Instance.Theme.AutoCompleteFgExtra);
            
            var mousePos = PointFromScreen(Mouse.Position);
            int mouseRow = -1;
            if (mousePos.X >= 0.0f && mousePos.X <= ClientSize.Width) {
                mouseRow = (int)(mousePos.Y / height);
            }
            
            for (int row = minRow; row <= maxRow; row++) {
                var entry = menu.shownEntries[row];
                
                if (row == menu.SelectedEntry) {
                    e.Graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteSelected, 0.0f, row * height, width, height);
                } else if (row == mouseRow) {
                    e.Graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteHovered, 0.0f, row * height, width, height);
                }
                
                e.Graphics.DrawText(font, displayBrush, EntryPaddingLeft, row * height + EntryBetweenPadding, entry.DisplayText);
                e.Graphics.DrawText(font, extraBrush, EntryPaddingLeft + font.CharWidth() * (maxDisplayLen + DisplayExtraPadding), row * height + EntryBetweenPadding, entry.ExtraText);
            }
            
            base.OnPaint(e);
        }
        
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Buttons.HasFlag(MouseButtons.Primary)) {
                int mouseRow = (int)(e.Location.Y / menu.EntryHeight);
                
                if (mouseRow >= 0 && mouseRow < menu.shownEntries.Length) {
                    menu.shownEntries[mouseRow].OnUse();
                    e.Handled = true;
                }
            }
            
            base.OnMouseDown(e);
        }
        
        protected override void OnMouseWheel(MouseEventArgs e) {
            if (Settings.Instance.ScrollSpeed > 0.0f) {
                // Manually scroll to respect our scroll speed 
                menu.ScrollPosition = menu.ScrollPosition with {
                    Y = Math.Clamp((int)(menu.ScrollPosition.Y - e.Delta.Height * menu.EntryHeight * Settings.Instance.ScrollSpeed), 0, Height - menu.ClientSize.Height)
                };
                e.Handled = true;
            }

            base.OnMouseWheel(e);
        }
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
            selectedEntry = Math.Clamp(value, 0, shownEntries.Length);

            ScrollIntoView();
            drawable.Invalidate();
        }
    }
    
    public int ContentWidth {
        set {
            if (Eto.Platform.Instance.IsWpf) {
                const int scrollBarWidth = 17;
                bool scrollBarVisible = Height <= ContentHeight;
                Width = Math.Max(0, value + BorderWidth * 2 + (scrollBarVisible ? scrollBarWidth : 0));
            } else if (Eto.Platform.Instance.IsMac) {
                const int scrollBarWidth = 15;
                bool scrollBarVisible = Height <= ContentHeight;
                Width = Math.Max(0, value + BorderWidth * 2 + (scrollBarVisible ? scrollBarWidth : 0));
            } else {
                Width = Math.Max(0, value + BorderWidth * 2);
            }
        }
        get {
            if (shownEntries.Length == 0) {
                return 0;
            }
            
            var font = FontManager.EditorFontRegular;
            int maxDisplayLen = shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);
            int maxExtraLen = shownEntries.Select(entry => entry.ExtraText.Length).Aggregate(Math.Max);
            
            return (int)(font.CharWidth() * (maxDisplayLen + DisplayExtraPadding + maxExtraLen) + EntryPaddingLeft + EntryPaddingRight);
        }
    }
    public int ContentHeight {
        set => Height = Math.Max(0, value + BorderWidth * 2);
        get => shownEntries.Length * EntryHeight;
    }
    
    public int EntryHeight => (int)(FontManager.EditorFontRegular.LineHeight() + EntryBetweenPadding * 2.0f);
    
    private Entry[] shownEntries = [];
    private readonly ContentDrawable drawable;
    
    public AutoCompleteMenu() {
        drawable = new ContentDrawable(this);
        Content = drawable;

        Recalc();
    }
    
    private void Recalc() {
        shownEntries = entries.Where(entry => entry.SearchText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
        if (shownEntries.Length == 0) {
            Visible = false;
            return;
        }
        
        selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);

        drawable.Size = new(ContentWidth, ContentHeight);
        drawable.Invalidate();
    }
    
    private void ScrollIntoView() {
        const int lookAhead = 2;
        
        int entryHeight = EntryHeight;
        int scrollStartTop = ScrollPosition.Y + lookAhead * entryHeight;
        int scrollStartBottom = ScrollPosition.Y + ClientSize.Height - lookAhead * entryHeight;
        
        int selectedTop = SelectedEntry * entryHeight;
        int selectedBottom = selectedTop + entryHeight;
        
        if (selectedTop < scrollStartTop) {
            ScrollPosition = ScrollPosition with { Y = Math.Max(0, selectedTop - lookAhead * entryHeight) };
        } else if (selectedBottom > scrollStartBottom) {
            ScrollPosition = ScrollPosition with { Y = Math.Min(shownEntries.Length * entryHeight - ClientSize.Height, selectedBottom + lookAhead * entryHeight - ClientSize.Height) }; 
        }
    }
    
    public bool HandleKeyDown(KeyEventArgs e, bool useTabComplete) {
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
        if (e.Key == Keys.Enter || useTabComplete && e.Key == Keys.Tab) {
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
