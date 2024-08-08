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
    
    private const int DisplayExtraPadding = 1;
    
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

    private static float BorderRounding => 7.0f;
    private static float BorderPadding => 7.0f;
    
    private static float EntryRounding => 5.0f;
    private static float EntryPaddingHorizontal => 5.0f;
    private static float EntryPaddingVertical => 4.5f;
    
    private sealed class ContentDrawable : Drawable {
        private readonly PopupMenu menu;
        
        public ContentDrawable(PopupMenu menu) {
            this.menu = menu;
            
            BackgroundColor = Colors.Transparent; // Draw background ourselves to apply rounded corners
            Settings.ThemeChanged += () => BackgroundColor = Settings.Instance.Theme.AutoCompleteBg;
            
            MouseEnter += (_, _) => Invalidate();
            MouseLeave += (_, _) => Invalidate();
            menu.Scroll += (_, _) => Invalidate();
        }
        
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.FillPath(
                Settings.Instance.Theme.AutoCompleteBg, 
                GraphicsPath.GetRoundRect(new RectangleF(menu.ScrollPosition.X, menu.ScrollPosition.Y, menu.Width, menu.Height), BorderRounding));
            
            if (menu.shownEntries.Length == 0) {
                return;
            }

            var font = FontManager.PopupFont;
            int maxDisplayLen = menu.shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max);

            float width = menu.ContentWidth - BorderPadding * 2.0f;
            float height = menu.EntryHeight;
            
            const int rowCullOverhead = 3;
            int minRow = Math.Max(0, (int)(menu.ScrollPosition.Y / height) - rowCullOverhead);
            int maxRow = Math.Min(menu.shownEntries.Length - 1, (int)((menu.ScrollPosition.Y + menu.ClientSize.Height) / height) + rowCullOverhead);
            
            using var displayEnabledBrush = new SolidBrush(Settings.Instance.Theme.AutoCompleteFg);
            using var displayDisabledBrush = new SolidBrush(Settings.Instance.Theme.AutoCompleteFgDisabled);
            using var extraBrush = new SolidBrush(Settings.Instance.Theme.AutoCompleteFgExtra);
            
            var mousePos = PointFromScreen(Mouse.Position);
            int mouseRow = -1;
            if (mousePos.X >= 0.0f && mousePos.X <= ClientSize.Width - menu.ScrollBarWidth) {
                mouseRow = (int)((mousePos.Y - BorderPadding) / height);
            }
            
            for (int row = minRow; row <= maxRow; row++) {
                var entry = menu.shownEntries[row];
                
                if (row == menu.SelectedEntry && !entry.Disabled) {
                    e.Graphics.FillPath(Settings.Instance.Theme.AutoCompleteSelected, GraphicsPath.GetRoundRect(new RectangleF(BorderPadding, row * height + BorderPadding, width, height), EntryRounding));
                } else if (row == mouseRow && !menu.shownEntries[mouseRow].Disabled) {
                    e.Graphics.FillPath(Settings.Instance.Theme.AutoCompleteHovered, GraphicsPath.GetRoundRect(new RectangleF(BorderPadding, row * height + BorderPadding, width, height), EntryRounding));
                }

                var displayBrush = entry.Disabled ? displayDisabledBrush : displayEnabledBrush;
                e.Graphics.DrawText(font, displayBrush, EntryPaddingHorizontal + BorderPadding, row * height + EntryPaddingVertical + BorderPadding, entry.DisplayText);
                e.Graphics.DrawText(font, extraBrush, EntryPaddingHorizontal + BorderPadding + font.CharWidth() * (maxDisplayLen + DisplayExtraPadding), row * height + EntryPaddingVertical + BorderPadding, entry.ExtraText);
            }
            
            base.OnPaint(e);
        }
        
        protected override void OnMouseMove(MouseEventArgs e) {
            int mouseRow = (int)((e.Location.Y - BorderPadding) / menu.EntryHeight);
            if (mouseRow >= 0 && mouseRow < menu.shownEntries.Length && !menu.shownEntries[mouseRow].Disabled) {
                Cursor = Cursors.Pointer;
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

            ScrollIntoView();
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
            
            return (int)(font.CharWidth() * (maxDisplayLen + DisplayExtraPadding + maxExtraLen) + EntryPaddingHorizontal + EntryPaddingHorizontal + BorderPadding * 2);
        }
    }
    public int ContentHeight {
        set => Height = Math.Max(0, value);
        get => (int)(shownEntries.Length * EntryHeight + BorderPadding * 2);
    }
    
    public int EntryHeight => (int)(FontManager.PopupFont.LineHeight() + EntryPaddingVertical * 2.0f);
    
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

    private void MoveSelection(int direction) {
        for (int nextSelection = (SelectedEntry + direction).Mod(shownEntries.Length);
             nextSelection != SelectedEntry;
             nextSelection = (nextSelection + direction).Mod(shownEntries.Length)) {
            if (shownEntries[nextSelection].Disabled) continue;

            SelectedEntry = nextSelection;
            return;
        }
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