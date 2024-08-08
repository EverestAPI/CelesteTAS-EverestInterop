using System;
using System.Collections.Generic;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Editing;

public sealed class PopupMenu : Scrollable {
    public record Entry {
        /// The text which will be used for filtering results.
        public required string SearchText;
        /// The text which will be displayed inside the menu.
        public required string DisplayText;
        /// The extra text which will be displayed to the right of the main text.
        public required string ExtraText;
        /// Callback for when this entry is selected.
        public required Action OnUse;
        /// Whether the entry can be selected.
        public bool Disabled = false;
    }
    
    /// Spacing between the longest DisplayText and ExtraText of entries in characters
    private const int DisplayExtraPadding = 2;
    
    private int ScrollBarWidth {
        get {
            bool scrollBarVisible = Height < ContentHeight && Height > 0;
            if (!scrollBarVisible) {
                return 0;
            }
            
            if (Eto.Platform.Instance.IsWpf) {
                return 17;
            }
            if (Eto.Platform.Instance.IsGtk) {
                return 17; // This probably relies on the GTK theme, but being slight off isn't too big of an issue
            }
            if (Eto.Platform.Instance.IsMac) {
                return 15;
            }
            return 0;
        }
    }
    
    private sealed class ContentDrawable : Drawable {
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
        
        protected override void OnPaint(PaintEventArgs e) { 
            e.Graphics.FillPath(
                Settings.Instance.Theme.PopupMenuBg, 
                GraphicsPath.GetRoundRect(new RectangleF(menu.ScrollPosition.X, menu.ScrollPosition.Y, menu.Width, menu.Height), Settings.Instance.Theme.PopupMenuBorderRounding));
            
            if (menu.shownEntries.Length == 0) {
                return;
            }

            var font = FontManager.PopupFont;
            int maxDisplayLen = menu.shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);

            float width = menu.ContentWidth - Settings.Instance.Theme.PopupMenuBorderPadding * 2.0f;
            float height = menu.EntryHeight;
            
            const int rowCullOverhead = 3;
            int minRow = Math.Max(0, (int)(menu.ScrollPosition.Y / height) - rowCullOverhead);
            int maxRow = Math.Min(menu.shownEntries.Length - 1, (int)((menu.ScrollPosition.Y + menu.ClientSize.Height) / height) + rowCullOverhead);
            
            using var displayEnabledBrush = new SolidBrush(Settings.Instance.Theme.PopupMenuFg);
            using var displayDisabledBrush = new SolidBrush(Settings.Instance.Theme.PopupMenuFgDisabled);
            using var extraBrush = new SolidBrush(Settings.Instance.Theme.PopupMenuFgExtra);
            
            for (int row = minRow; row <= maxRow; row++) {
                var entry = menu.shownEntries[row];
                
                if (row == menu.SelectedEntry && !entry.Disabled) {
                    e.Graphics.FillPath(
                        Settings.Instance.Theme.PopupMenuSelected, 
                        GraphicsPath.GetRoundRect(
                            new RectangleF(Settings.Instance.Theme.PopupMenuBorderPadding, 
                                row * height + Settings.Instance.Theme.PopupMenuBorderPadding + Settings.Instance.Theme.PopupMenuEntrySpacing / 2.0f, 
                                width, 
                                height - Settings.Instance.Theme.PopupMenuEntrySpacing), 
                            Settings.Instance.Theme.PopupMenuEntryRounding));
                }

                var displayBrush = entry.Disabled ? displayDisabledBrush : displayEnabledBrush;
                e.Graphics.DrawText(font, displayBrush,
                    Settings.Instance.Theme.PopupMenuBorderPadding + Settings.Instance.Theme.PopupMenuEntryHorizontalPadding,
                    Settings.Instance.Theme.PopupMenuBorderPadding + row * height + Settings.Instance.Theme.PopupMenuEntryVerticalPadding + Settings.Instance.Theme.PopupMenuEntrySpacing / 2.0f, 
                    entry.DisplayText);
                e.Graphics.DrawText(font, extraBrush,
                    Settings.Instance.Theme.PopupMenuBorderPadding + Settings.Instance.Theme.PopupMenuEntryHorizontalPadding + font.CharWidth() * (maxDisplayLen + DisplayExtraPadding),
                    Settings.Instance.Theme.PopupMenuBorderPadding + row * height + Settings.Instance.Theme.PopupMenuEntryVerticalPadding + Settings.Instance.Theme.PopupMenuEntrySpacing / 2.0f, 
                    entry.ExtraText);
            }
            
            Size = new(menu.ContentWidth, menu.ContentHeight);
            
            base.OnPaint(e);
        }
        
        protected override void OnMouseMove(MouseEventArgs e) {
            int mouseRow = (int)((e.Location.Y - Settings.Instance.Theme.PopupMenuBorderPadding) / menu.EntryHeight);
            if (mouseRow >= 0 && mouseRow < menu.shownEntries.Length && !menu.shownEntries[mouseRow].Disabled) {
                Cursor = Cursors.Pointer;
                
                if ((e.Location - lastMouseSelection).LengthSquared >= MinMouseTravelDistance * MinMouseTravelDistance) {
                    menu.SelectedEntry = mouseRow;
                    lastMouseSelection = e.Location;
                }
            } else {
                Cursor = null;
            }
            
            Invalidate();
            base.OnMouseMove(e);
        }
        
        protected override void OnMouseDown(MouseEventArgs e) {
            e.Handled = true;
            
            if (e.Buttons.HasFlag(MouseButtons.Primary)) {
                int mouseRow = (int)(e.Location.Y / menu.EntryHeight);
                if (mouseRow >= 0 && mouseRow < menu.shownEntries.Length && !menu.shownEntries[mouseRow].Disabled) {
                    menu.shownEntries[mouseRow].OnUse();
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
            drawable.Invalidate();
        }
    }
    
    public int ContentWidth {
        set => Width = Math.Max(0, value + ScrollBarWidth);
        get {
            if (shownEntries.Length == 0) {
                return 0;
            }
            
            var font = FontManager.PopupFont;
            int maxDisplayLen = shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);
            int maxExtraLen = shownEntries.Select(entry => entry.ExtraText.Length).Aggregate(Math.Max);
            
            return (int)(font.CharWidth() * (maxDisplayLen + DisplayExtraPadding + maxExtraLen) + Settings.Instance.Theme.PopupMenuEntryHorizontalPadding * 2.0f + Settings.Instance.Theme.PopupMenuBorderPadding * 2);
        }
    }
    public int ContentHeight {
        set => Height = Math.Max(0, value);
        get => shownEntries.Length * EntryHeight + Settings.Instance.Theme.PopupMenuBorderPadding * 2;
    }
    
    public int EntryHeight => (int)(FontManager.PopupFont.LineHeight() + Settings.Instance.Theme.PopupMenuEntryVerticalPadding * 2.0f + Settings.Instance.Theme.PopupMenuEntrySpacing);
    
    private Entry[] shownEntries = [];
    private readonly ContentDrawable drawable;
    
    public PopupMenu() {
        drawable = new ContentDrawable(this);
        Content = drawable;
        Border = BorderType.None;

        Recalc();
    }
    
    private void Recalc() {
        shownEntries = entries.Where(entry => entry.SearchText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
        if (shownEntries.Length == 0) {
            Visible = false;
            return;
        }
        
        selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);

        // The +1 is a hack-fix for GTK.
        // If the content isn't large enough to require a scrollbar, GTK will just have the background be black instead of transparent.
        // Even weirder, once it was scrollable once, the black background will never come back...
        // The proper size is set at the end of ContentDrawable.Paint()
        drawable.Size = new(ContentWidth, ContentHeight + 1);
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
    
    public bool HandleKeyDown(KeyEventArgs e, bool useTabComplete) {
        if (!Visible)
            return false;
        
        if (e.Key == Keys.Up) {
            MoveSelection(-1);
            return true;
        }
        if (e.Key == Keys.Down) {
            MoveSelection(1);
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