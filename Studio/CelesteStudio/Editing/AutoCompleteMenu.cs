using System;
using System.Collections.Generic;
using System.Linq;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Editing;

public class AutoCompleteMenu {
    public record Entry {
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
            shownEntries = Entries.Where(entry => entry.DisplayText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
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
            shownEntries = Entries.Where(entry => entry.DisplayText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
            if (shownEntries.Length == 0) {
                visible = false;
                return;
            }
            
            selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);
        }
    }
    
    private int maxEntries = 0;
    private float scrollOffset = 0.0f;
    
    public bool OnKeyDown(KeyEventArgs e) {
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
    
    public void Draw(Graphics graphics, Font font, float x, float y, float maxHeight) {
        if (!Visible)
            return;
        
        const float extraHeight = 0.25f;
        const float entryPadding = 2.0f;
        const float borderWidth = 2.0f;
        
        maxEntries = (int)Math.Floor((maxHeight - entryPadding) / (font.LineHeight() + entryPadding));
        ScrollIntoView();
        
        float boxX = x;
        float boxY = y;
        float boxW = font.CharWidth() * shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max) + entryPadding * 2.0f;
        float boxH = (font.LineHeight() + entryPadding) * maxEntries + entryPadding;
        
        // Shown next entries when you can scroll
        if (scrollOffset > 0.0f) {
            y += extraHeight * 2.0f * font.LineHeight();
            boxH += extraHeight * 2.0f * font.LineHeight();
        }
        if (maxEntries + scrollOffset < shownEntries.Length) {
            boxH += extraHeight * 2.0f * font.LineHeight();
        }
        
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteBorder, boxX - borderWidth, boxY - borderWidth, boxW + borderWidth * 2.0f, boxH + borderWidth * 2.0f);
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteBg, boxX, boxY, boxW, boxH);

        graphics.SetClip(new RectangleF(boxX, boxY, boxW, boxH));
        float yOff = entryPadding - scrollOffset * (font.LineHeight() + entryPadding);
        foreach (var entry in shownEntries) {
            graphics.DrawText(font, Settings.Instance.Theme.AutoCompleteFg, x + entryPadding, y + yOff, entry.DisplayText);
            yOff += font.LineHeight() + entryPadding;
        }
        graphics.ResetClip();
        
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteSelected, x, y + (font.LineHeight() + entryPadding) * (SelectedEntry - scrollOffset), boxW, font.LineHeight() + entryPadding * 2.0f);
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