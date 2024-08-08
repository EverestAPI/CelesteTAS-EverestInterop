using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;
using System.Text;
using CelesteStudio.Util;
using StudioCommunication;

namespace CelesteStudio.Editing.ContextActions;

public class CreateRepeat : ContextAction {
    public override string Name => "Create Repeat from selection";
    
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
        string foundPattern = "";
        
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
            StringBuilder foundPatternBuilder = new();
            for (int i = 0; i < patternLength; i++) {
                foundPatternBuilder.Append(Document.Lines[minRow + i] + Document.NewLine);
            }
            foundPattern = foundPatternBuilder.ToString();
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
            Document.InsertLine(minRow, $"Repeat{separator}{foundRepeatCount}{Document.NewLine}{foundPattern}EndRepeat");
            
            Document.Selection.Clear();
            Document.Caret.Row = minRow;
            Document.Caret.Col = 0;
        });
    }
}