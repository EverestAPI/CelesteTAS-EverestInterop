using System;
using System.Collections.Generic;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Editing;

public class AutoCompleteMenu {
    public record Entry {
        public required string SearchText;
        public required string DisplayText;
        public required Action OnUse;
    }
    
    private bool visible = false;
    public bool Visible {
        get => visible;
        set {
            visible = value;
            filter = string.Empty;
            selectedEntry = 0;
            scrollOffset = 0;
        }
    }
    
    private List<Entry> entries = [];
    public List<Entry> Entries {
        get => entries;
        set {
            entries = value;
            shownEntries = Entries.Where(entry => entry.SearchText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
            if (shownEntries.Length == 0) {
                visible = false;
                return;
            }
            
            selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);
        }
    }
    
    private Entry[] shownEntries = [];
    
    private int selectedEntry;
    public int SelectedEntry {
        get => selectedEntry;
        set => selectedEntry = Math.Clamp(value, 0, shownEntries.Length);
    }
    
    private string filter = string.Empty;
    public string Filter {
        get => filter;
        set {
            filter = value;
            shownEntries = Entries.Where(entry => entry.SearchText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
            if (shownEntries.Length == 0) {
                visible = false;
                return;
            }
            
            selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);
        }
    }
    
    private int maxEntries = 0;
    private float scrollOffset = 0.0f;
    
    public bool HandleKeyDown(KeyEventArgs e) {
        if (!Visible)
            return false;
        
        if (e.Key == Keys.Up) {
            SelectedEntry = (SelectedEntry - 1).Mod(shownEntries.Length);
            ScrollIntoView();
            return true;
        }
        if (e.Key == Keys.Down) {
            SelectedEntry = (SelectedEntry + 1).Mod(shownEntries.Length);
            ScrollIntoView();
            return true;
        }
        if (e.Key == Keys.Enter) {
            shownEntries[SelectedEntry].OnUse();
            return true;
        }
        if (e.Key == Keys.Escape) {
            Visible = false;
            return true;
        }
        
        return false;
    }
    
    private const float ExtraHeight = 0.25f;
    private const float EntryPadding = 2.0f;
    private const float BorderWidth = 2.0f;
    private const float ScrollBarPadding = 5.0f;
    private const float ScrollBarWidth = 20.0f;
    private const float ScrollBarExtend = 5.0f; 
    
    public PointF MouseLocation;
    public bool DraggingScrollBar { get; private set; } = false;
    private float dragStartY; 
    private float dragStartScroll; 
    
    public bool HandleMouseDown(PointF location, Font font, float x, float y, float w, float h) {
        // Drag scroll bar
        if (maxEntries < shownEntries.Length) {
            float barY = (scrollOffset / shownEntries.Length) * h;
            float barH = (maxEntries / (float)shownEntries.Length) * h;
            
            if (location.X >= x + w - ScrollBarWidth &&
                location.Y >= y + barY - ScrollBarExtend && location.Y <= y + barY + barH + ScrollBarExtend)
            {
                DraggingScrollBar = true;
                dragStartY = location.Y;
                dragStartScroll = scrollOffset;
                return true;
            }
        }
        
        // Select entries
        int idx = (int)((location.Y - y) / (font.LineHeight() + EntryPadding) + scrollOffset);
        if (idx >= 0 && idx < shownEntries.Length) {
            shownEntries[idx].OnUse();
            return true;
        }
        
        return false;
    }
    
    public bool HandleMouseUp() {
        if (DraggingScrollBar) {
            DraggingScrollBar = false;
            return true;
        }
        
        return false;
    }
    
    public void HandleMouseMove(PointF location, Font font, float x, float y, float w, float h, out Cursor? cursor) {
        // Drag scroll bar
        if (DraggingScrollBar) {
            float delta = (location.Y - dragStartY) / h * shownEntries.Length;
            scrollOffset = Math.Clamp(dragStartScroll + delta, 0.0f, shownEntries.Length - maxEntries);
            
            cursor = null;
            return;
        }
        if (maxEntries < shownEntries.Length) {
            float barY = (scrollOffset / shownEntries.Length) * h;
            float barH = (maxEntries / (float)shownEntries.Length) * h;
            
            if (location.X >= x + w - ScrollBarWidth &&
                location.Y >= y + barY - ScrollBarExtend && location.Y <= y + barY + barH + ScrollBarExtend)
            {
                cursor = null;
                return;
            }
        }

        cursor = Cursors.Pointer;
    }
    
    public void HandleMouseWheel(float delta) {
        if (maxEntries < shownEntries.Length) {
            scrollOffset = Math.Clamp(scrollOffset - delta, 0.0f, shownEntries.Length - maxEntries);
        }
    }
    
    public (float X, float Y, float Width, float Height) Measure(Font font, float x, float y, float maxHeight) {
        maxEntries = (int)Math.Floor((maxHeight - EntryPadding) / (font.LineHeight() + EntryPadding));
        
        float boxW = font.CharWidth() * shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max) + EntryPadding * 2.0f;
        float boxH = (font.LineHeight() + EntryPadding) * Math.Min(shownEntries.Length, maxEntries) + EntryPadding;
        
        // Shown next entries when you can scroll
        if (scrollOffset > 0.0f) {
            y += ExtraHeight * 2.0f * font.LineHeight();
            boxH += ExtraHeight * 2.0f * font.LineHeight();
        }
        if (maxEntries + scrollOffset < shownEntries.Length) {
            boxH += ExtraHeight * 2.0f * font.LineHeight();
        }
        // Add scroll bar
        if (maxEntries < shownEntries.Length) {
            boxW += ScrollBarPadding + ScrollBarWidth;
        }
        
        return (x, y, boxW, boxH);
    }
    
    public void Draw(Graphics graphics, Font font, float x, float y, float maxHeight) {
        if (!Visible)
            return;
        
        float boxX = x;
        float boxY = y;
        (x, y, float boxW, float boxH) = Measure(font, x, y, maxHeight);
        
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteBorder, boxX - BorderWidth, boxY - BorderWidth, boxW + BorderWidth * 2.0f, boxH + BorderWidth * 2.0f);
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteBg, boxX, boxY, boxW, boxH);

        graphics.SetClip(new RectangleF(boxX, boxY, boxW, boxH));
        float yOff = EntryPadding - scrollOffset * (font.LineHeight() + EntryPadding);
        foreach (var entry in shownEntries) {
            // Cull off-screen entries
            if (y + yOff + font.LineHeight() + EntryPadding < boxY || y + yOff > boxY + boxH) {
                yOff += font.LineHeight() + EntryPadding;
                continue;
            }
            
            graphics.DrawText(font, Settings.Instance.Theme.AutoCompleteFg, x + EntryPadding, y + yOff, entry.DisplayText);
            yOff += font.LineHeight() + EntryPadding;
        }
        yOff = EntryPadding - scrollOffset * (font.LineHeight() + EntryPadding);
        
        // Highlight selected
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteSelected, x, y + yOff + (font.LineHeight() + EntryPadding) * SelectedEntry, boxW, font.LineHeight() + EntryPadding * 2.0f);
        
        float barY = (scrollOffset / shownEntries.Length) * boxH;
        float barH = (maxEntries / (float)shownEntries.Length) * boxH;
        
        // Highlight hovered
        int idx = (int)((MouseLocation.Y - y) / (font.LineHeight() + EntryPadding) + scrollOffset);
        bool hoveringBar = maxEntries < shownEntries.Length &&
                           (MouseLocation.X >= boxX + boxW - ScrollBarWidth && MouseLocation.X <= boxX + boxW &&
                            MouseLocation.Y >= boxY + barY && MouseLocation.Y <= boxY + barY + barH) ||
                           DraggingScrollBar;
        
        if (MouseLocation.X >= x && MouseLocation.X <= x + boxW &&
            !hoveringBar &&
            idx >= 0 && idx < shownEntries.Length) 
        {
            const float shrink = 2.0f; // Avoids overlap with the selected line
            graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteHovered, x, y + yOff + (font.LineHeight() + EntryPadding) * idx + shrink, boxW, font.LineHeight() + EntryPadding * 2.0f - shrink * 2.0f);
        }

        graphics.ResetClip();
        
        if (maxEntries < shownEntries.Length) {
            var color = hoveringBar ? Settings.Instance.Theme.AutoCompleteScrollBarHovered : Settings.Instance.Theme.AutoCompleteScrollBar;
            graphics.FillRectangle(color, boxX + boxW - ScrollBarWidth, boxY + barY, ScrollBarWidth, barH);
        }
    }
    
    private void ScrollIntoView() {
        if (maxEntries == 0) {
            // The actual size hasn't been determined yet
            return;
        }
        
        const int lookAhead = 1;
        int visualEntry = (int)(SelectedEntry - scrollOffset);
        
        if (visualEntry > maxEntries - 1 - lookAhead) {
            scrollOffset = Math.Min(shownEntries.Length - maxEntries, scrollOffset + (visualEntry - (maxEntries - 1 - lookAhead)));
        } else if (visualEntry < lookAhead) {
            scrollOffset = Math.Max(0, scrollOffset + (visualEntry - lookAhead));
        }
    }
}