using System.Diagnostics;
using CelesteStudio.Data;
using CelesteStudio.Util;

namespace CelesteStudio.Editing.ContextActions;

public class CreateRepeat : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_CreateRepeatCommand;
    
    public override PopupMenu.Entry? Check() {
        if (Document.Selection.Empty) {
            return null;
        }
        
        int minRow = Document.Selection.Min.Row;
        int maxRow = Document.Selection.Max.Row;

        // Trim empty lines
        while (string.IsNullOrWhiteSpace(Document.Lines[minRow]) && minRow < maxRow) 
            minRow++;
        while (string.IsNullOrWhiteSpace(Document.Lines[maxRow]) && maxRow > minRow) 
            maxRow--;

        int rowCount = maxRow - minRow + 1;
        if (rowCount <= 1) {
            return null;
        }

        int foundRepeatCount = 0;
        string[] foundPattern = [];
        
        for (int patternLength = 1; patternLength <= rowCount / 2; patternLength++) {
            // Pattern needs to occur an integer amount of times
            if (rowCount % patternLength != 0) {
                continue;
            }
            
            int repeatCount = rowCount / patternLength;

            // Check that each repetiotion matches
            for (int patternIdx = 0; patternIdx < patternLength; patternIdx++) {
                string reference = Document.Lines[minRow + patternIdx];

                for (int patternRep = 1; patternRep < repeatCount; patternRep++) {
                    string repetition = Document.Lines[minRow + patternIdx + patternRep * patternLength];

                    if (repetition != reference) {
                        // No match
                        goto NextIter;
                    }
                }
            }

            // Pattern matches
            foundPattern = Document.Lines.GetArrayRange(minRow..(minRow + patternLength));
            foundRepeatCount = repeatCount;
            break;
            
            NextIter:;
        }

        if (foundRepeatCount <= 1) {
            return null;
        }

        return CreateEntry("", () => {
            using var __ = Document.Update();
            
            var separator = Settings.Instance.CommandSeparator switch {
                CommandSeparator.Space => " ",
                CommandSeparator.Comma => ",",
                CommandSeparator.CommaSpace => ", ",
                _ => throw new UnreachableException()
            };
                
            Document.RemoveLines(minRow, maxRow);
            Document.InsertLines(minRow, [
                $"Repeat{separator}{foundRepeatCount}",
                ..foundPattern,
                "EndRepeat",
            ]);
            
            Document.Selection.Clear();
            // Move cursor to first input inside Repeat
            Document.Caret.Row = minRow + 1;
            Document.Caret.Col = 0;
            
            // Snap to frame count
            if (foundPattern.Length > 0 && ActionLine.TryParse(foundPattern[0], out _)) {
                Document.Caret.Col = ActionLine.MaxFramesDigits;
            }
        });
    }
}