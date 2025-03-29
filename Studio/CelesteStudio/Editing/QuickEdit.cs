using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CelesteStudio.Editing;

public record struct QuickEdit {
    private static readonly Dictionary<string, QuickEdit> quickEditCache = new();

    public string ActualText;
    public Selection[] Selections;

    public static QuickEdit? Parse(string text) {
        if (quickEditCache.TryGetValue(text, out var quickEdit)) {
            return quickEdit;
        }

        text = text.ReplaceLineEndings($"{Document.NewLine}");

        var actualText = new StringBuilder(capacity: text.Length);
        var quickEditSpots = new Dictionary<int, Selection>();

        int row = 0;
        int col = 0;
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c == Document.NewLine) {
                actualText.Append(c);
                row++;
                col = 0;
                continue;
            }
            if (c != '[') {
                actualText.Append(c);
                col++;
                continue;
            }

            int endIdx = text.IndexOf(']', i);
            var quickEditText = text[(i + 1)..endIdx];

            int delimIdx = quickEditText.IndexOf(';');
            if (delimIdx < 0) {
                int idx = -1;
                if (int.TryParse(quickEditText, out int editIdx)) {
                    idx = editIdx;
                } else {
                    // Invalid format
                    return null;
                }
                quickEditSpots[idx] = new Selection { Start = new CaretPosition(row, col), End = new CaretPosition(row, col) };
            } else {
                int idx = int.Parse(quickEditText[..delimIdx]);
                var editableText = quickEditText[(delimIdx + 1)..];
                quickEditSpots[idx] = new Selection { Start = new CaretPosition(row, col), End = new CaretPosition(row, col + editableText.Length) };
                actualText.Append(editableText);
                col += editableText.Length;
            }

            i = endIdx;
        }

        // Convert to actual array
        var quickEditSelections = new Selection[quickEditSpots.Count];
        int index = 0;
        foreach (var spot in quickEditSpots.OrderBy(pair => pair.Key).Select(pair => pair.Value)) {
            quickEditSelections[index] = spot;
            index++;
        }

        quickEdit = new QuickEdit { ActualText = actualText.ToString(), Selections = quickEditSelections };
        quickEditCache[text] = quickEdit;
        return quickEdit;
    }
}
