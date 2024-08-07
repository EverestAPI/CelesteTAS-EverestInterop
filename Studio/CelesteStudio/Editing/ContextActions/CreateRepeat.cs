using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;
using System.Text;
using CelesteStudio.Util;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class CreateRepeat : ContextAction {
    public override string Name => "Create Repeat from selection";
    
    public override AutoCompleteMenu.Entry? Check() {
        if (Document.Selection.Empty) return null;
        
        int startRow = Document.Selection.Min.Row;
        int endRow = Document.Selection.Max.Row;

        while (Document.Lines[startRow].Trim().Length == 0 && startRow < endRow) startRow++;
        while (Document.Lines[endRow].Trim().Length == 0 && endRow > startRow) endRow--;

        int rowCount = (endRow - startRow) + 1;

        if (rowCount <= 1) {
            return null;
        }

        int foundRepeatCount = 0;
        string foundPattern = "";
        
        for (int patternLength = 1; patternLength <= rowCount / 2; patternLength++) {
            if (rowCount % patternLength != 0) continue;

            int repeatCount = rowCount / patternLength;
            bool matchesPattern = true;

            for (int patternIdx = 0; patternIdx < patternLength; patternIdx++) {
                string reference = Document.Lines[startRow + patternIdx];

                for (int patternRep = 1; patternRep < repeatCount; patternRep++) {
                    string repetition = Document.Lines[startRow + patternIdx + patternRep * patternLength];
                    if (repetition != reference) {
                        matchesPattern = false;
                        break;
                    }
                }
            }

            if (matchesPattern) {
                StringBuilder foundPatternBuilder = new();
                for (int i = 0; i < patternLength; i++) {
                    foundPatternBuilder.Append(Document.Lines[startRow + i]);
                    foundPatternBuilder.Append(Document.NewLine);
                }
                foundPattern = foundPatternBuilder.ToString();
                foundRepeatCount = repeatCount;
                break;
            }
        }

        if (foundRepeatCount == 0) {
            return null;
        }

        return CreateEntry("", () => {
            using var __ = Document.Update();
            Document.RemoveLines(startRow, endRow);

            Document.InsertLine(
                startRow,
                $"""
                    StartRepeat,{foundRepeatCount}
                    {foundPattern}EndRepeat
                    """);
            
            Document.Selection.Clear();
            Document.Caret.Row = startRow;
            Document.Caret.Col = 0;
        });
    }
}