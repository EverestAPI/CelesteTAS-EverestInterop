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
            Filter = string.Empty;
            SelectedEntry = 0;
        }
    }
    
    private string filter = string.Empty;
    public string Filter {
        get => filter;
        set {
            filter = value;
            shownEntries = Entries.Where(entry => entry.DisplayText.StartsWith(filter, StringComparison.InvariantCultureIgnoreCase)).ToArray();
            if (shownEntries.Length == 0) {
                Visible = false;
                return;
            }
            
            selectedEntry = Math.Clamp(selectedEntry, 0, shownEntries.Length - 1);
        }
    }
    
    private int selectedEntry;
    public int SelectedEntry {
        get => selectedEntry;
        set => selectedEntry = Math.Clamp(value, 0, shownEntries.Length);
    }
    
    public readonly List<Entry> Entries = [];
    private Entry[] shownEntries = [];
    
    public bool OnKeyDown(KeyEventArgs e) {
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
        if (e.Key == Keys.Enter) {
            shownEntries[SelectedEntry].OnUse();
            Visible = false;
            return true;
        }
        
        return false;
    }
    
    public void Draw(Graphics graphics, Font font, float x, float y) {
        if (!Visible)
            return;
        
        const float autocompletePadding = 5.0f;
        const float autocompleteBorder = 2.0f;
        
        float boxW = font.CharWidth() * shownEntries.Select(entry => entry.DisplayText.Length).Aggregate(Math.Max) + autocompletePadding * 2.0f;
        float boxH = font.LineHeight() * shownEntries.Length + autocompletePadding * 2.0f;
        
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteBorder, x - autocompleteBorder, y - autocompleteBorder, boxW + autocompleteBorder * 2.0f, boxH + autocompleteBorder * 2.0f);
        graphics.FillRectangle(Settings.Instance.Theme.AutoCompleteBg, x, y, boxW, boxH);
        
        float yOff = 0.0f;
        foreach (var entry in shownEntries) {
            graphics.DrawText(font, Settings.Instance.Theme.AutoCompleteFg, x + autocompletePadding, y + yOff + autocompletePadding, entry.DisplayText);
            yOff += font.LineHeight();
        }
        
        graphics.FillRectangle(Color.FromArgb(0x7F0000FF), x, y + SelectedEntry * font.LineHeight() + autocompletePadding, boxW, font.LineHeight());
    }
}