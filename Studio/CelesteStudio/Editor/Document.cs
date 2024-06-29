using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CelesteStudio.Util;
using Eto;

namespace CelesteStudio;

public struct CaretPosition(int row = 0, int col = 0) {
    public int Row = row, Col = col;
    
    public static bool operator ==(CaretPosition lhs, CaretPosition rhs) => lhs.Row == rhs.Row && lhs.Col == rhs.Col;
    public static bool operator !=(CaretPosition lhs, CaretPosition rhs) => !(lhs == rhs);
    public static bool operator >(CaretPosition lhs, CaretPosition rhs) => lhs.Row > rhs.Row || (lhs.Row == rhs.Row && lhs.Col > rhs.Col);
    public static bool operator <(CaretPosition lhs, CaretPosition rhs) => lhs.Row < rhs.Row || (lhs.Row == rhs.Row && lhs.Col < rhs.Col);
    
    public override string ToString() => $"{Row}:{Col}";
    public override bool Equals(object? obj) => obj is CaretPosition other && Row == other.Row && Col == other.Col;
    public override int GetHashCode() => HashCode.Combine(Row, Col);
}

public enum CaretMovementType {
    None,
    CharLeft,
    CharRight,
    WordLeft,
    WordRight,
    LineUp,
    LineDown,
    PageUp,
    PageDown,
    LineStart,
    LineEnd,
    DocumentStart,
    DocumentEnd,
}

public struct Selection() {
    public CaretPosition Start = new(), End = new();
    
    public CaretPosition Min => Start < End ? Start : End;
    public CaretPosition Max => Start < End ? End : Start;
    public bool Empty => Start == End;
    
    public void Clear() => Start = End = new();
    public void Normalize() {
        // Ensures that Start <= End
        if (Start > End)
            (Start, End) = (End, Start);
    }
}

public class UndoStack(int stackSize = 256) {
    public struct Entry(List<string> lines, CaretPosition caret) {
        public readonly List<string> Lines = lines;
        public CaretPosition Caret = caret;
    }
    
    public int Curr = 0;
    public readonly Entry[] Stack = new Entry[stackSize];
    
    private int head = 0, tail = 0;
    
    public void Push(CaretPosition caret) {
        head = (Curr + 1) % stackSize;
        if (head == tail)
            tail = (tail + 1) % stackSize; // Discard the oldest entry
        
        Stack[Curr].Caret = caret;
        Stack[head] = new Entry(
            [..Stack[Curr].Lines], // Make sure to copy    
            caret // Will be overwritten later, so it doesn't matter what's used
        );
        Curr = head;
    }
    
    public void Undo() {
        if (Curr == tail) return;
        Curr = (Curr - 1) % stackSize;
    }
    public void Redo() {
        if (Curr == head) return;
        Curr = (Curr + 1) % stackSize;
    }
}

public class Document {
    // Unify all TASes to use a single line separator
    public const char NewLine = '\n';
    
    // Should only be used while an actual document is being loaded
    public static readonly Document Dummy = new(string.Empty);
    
    public CaretPosition Caret = new();
    public Selection Selection = new();
    
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string BackupDirectory {
        get {
            if (string.IsNullOrWhiteSpace(FilePath))
                return string.Empty;
            
            var backupBaseDir = Path.Combine(Settings.BaseConfigPath, "Backups");
            bool isBackupFile = Directory.GetParent(FilePath) is { } dir && dir.Parent?.FullName == backupBaseDir;
            
            string backupSubDir = isBackupFile 
                ? Directory.GetParent(FilePath)!.FullName 
                : $"{FileName}_{FilePath.GetStableHashCode()}";
            
            return Path.Combine(backupBaseDir, backupSubDir);
        }
    }

    private readonly UndoStack undoStack = new();

    private List<string> CurrentLines => undoStack.Stack[undoStack.Curr].Lines;
    public IReadOnlyList<string> Lines => CurrentLines.AsReadOnly();
    
    public string Text => string.Join(NewLine, CurrentLines);

    public bool Dirty { get; private set; }
    
    public event Action<Document, CaretPosition, CaretPosition> TextChanged = (doc, _, _) => {
        if (Settings.Instance.AutoSave) {
            doc.Save();
            return;
        }
        
        doc.Dirty = true;
    };
    public void OnTextChanged(CaretPosition min, CaretPosition max) => TextChanged.Invoke(this, min, max);
    
    private Document(string contents) {
        contents = contents.ReplaceLineEndings(NewLine.ToString());
        undoStack.Stack[undoStack.Curr] = new UndoStack.Entry(contents.SplitDocumentLines().ToList(), Caret);
        
        if (!string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath)) {
            // Save with the new line endings
            Save();    
        }
        
        Studio.CommunicationWrapper.Server.LinesUpdated += OnLinesUpdated;
    }

    ~Document() {
        Studio.CommunicationWrapper.Server.LinesUpdated -= OnLinesUpdated;
    }
    
    public void Dispose() {
        Studio.CommunicationWrapper.Server.LinesUpdated -= OnLinesUpdated;
    }

    public static Document? Load(string path) {
        try {
            string text = File.ReadAllText(path);
            return new Document(text) {
                FilePath = path,
            };
        } catch (Exception e) {
            Console.WriteLine(e);
        }

        return null;
    }

    public void Save() {
        try {
            File.WriteAllText(FilePath, Text);
            Dirty = false;
            
            if (Settings.Instance.AutoBackupEnabled && !string.IsNullOrWhiteSpace(FilePath))
                CreateBackup();
        } catch (Exception e) {
            Console.WriteLine(e);
        }
    }
    
    private void CreateBackup() {
        var backupDir = BackupDirectory;
        if (!Directory.Exists(backupDir))
            Directory.CreateDirectory(backupDir);
        
        string[] files = Directory.GetFiles(backupDir);
        if (files.Length > 0) {
            var lastFileTime = File.GetLastWriteTime(files.Last());
            
            // Wait until next interval
            if (Settings.Instance.AutoBackupRate > 0 && lastFileTime.AddMinutes(Settings.Instance.AutoBackupRate) >= DateTime.Now) {
                return;
            }
            
            // Delete the oldest backups until the desired count is reached
            if (Settings.Instance.AutoBackupCount > 0 && files.Length >= Settings.Instance.AutoBackupCount) {
                // Sort for oldest first
                Array.Sort(files, (a, b) => (File.GetLastWriteTime(b) - File.GetLastWriteTime(a)).Milliseconds);
                
                foreach (string path in files.Take(files.Length - Settings.Instance.AutoBackupCount + 1)) {
                    File.Delete(path);
                }
            }
        }
        
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(FilePath);
        
        // Apparently legacy Studio had a limit of 24 characters for the file name?
        // if (CurrentFileIsBackup() && fileNameWithoutExtension.Length > 24) {
        //     fileNameWithoutExtension = fileNameWithoutExtension.Substring(0, fileNameWithoutExtension.Length - 24);
        // }
        
        string backupFileName = Path.Combine(backupDir, fileNameWithoutExtension + DateTime.Now.ToString("_yyyy-MM-dd_HH-mm-ss-fff") + ".tas");
        File.Copy(FilePath, Path.Combine(backupDir, backupFileName));
    }

    private void OnLinesUpdated(Dictionary<int, string> newLines) {
        foreach ((int lineNum, string newText) in newLines) {
            if (lineNum < 0 || lineNum >= CurrentLines.Count)
                continue;
            
            CurrentLines[lineNum] = newText;
        }
    }

    #region Text Manipulation Helpers
    
    public void Undo() {
        undoStack.Undo();
        Caret = undoStack.Stack[undoStack.Curr].Caret;
    }
    public void Redo() {
        undoStack.Redo();
        Caret = undoStack.Stack[undoStack.Curr].Caret;
    }
    public void PushUndoState() {
        undoStack.Push(Caret);
    }
    
    public void Insert(string text) => Caret = Insert(Caret, text);
    public CaretPosition Insert(CaretPosition pos, string text) {
        var newLines = text.ReplaceLineEndings(NewLine.ToString()).SplitDocumentLines();
        if (newLines.Length == 0)
            return pos;
        
        var oldPos = pos;
        undoStack.Push(Caret);

        if (newLines.Length == 1) {
            CurrentLines[pos.Row] = CurrentLines[pos.Row].Insert(pos.Col, text);
            pos.Col += text.Length;
        } else {
            string left  = CurrentLines[pos.Row][..pos.Col];
            string right = CurrentLines[pos.Row][pos.Col..];
        
            CurrentLines[pos.Row] = left + newLines[0];
            for (int i = 1; i < newLines.Length; i++)
                CurrentLines.Insert(pos.Row + i, newLines[i]);
            pos.Row += newLines.Length - 1;
            pos.Col = newLines[^1].Length;
            CurrentLines[pos.Row] += right;
        }
        
        if (oldPos < pos)
            OnTextChanged(oldPos, pos);
        else
            OnTextChanged(pos, oldPos);
        return pos;
    }
    
    public void InsertLineAbove(string text) => InsertNewLine(Caret.Row, text);
    public void InsertLineBelow(string text) => InsertNewLine(Caret.Row + 1, text);
    public void InsertNewLine(int row, string text, bool raiseEvents = false) {
        if (raiseEvents) undoStack.Push(Caret);
        
        var newLines = text.SplitDocumentLines();
        if (newLines.Length == 0)
            CurrentLines.Insert(row, string.Empty);
        else
            CurrentLines.InsertRange(row, newLines);
        
        int newLineCount = Math.Max(0, newLines.Length - 1);
        
        if (Caret.Row >= row)
            Caret.Row += newLineCount;
        
        if (raiseEvents) OnTextChanged(new CaretPosition(row, 0), new CaretPosition(row + newLineCount, CurrentLines[row + newLineCount].Length));
    }
    
    public void ReplaceLine(int row, string text, bool raiseEvents = true) {
        if (raiseEvents) undoStack.Push(Caret);
        
        CurrentLines[row] = text;
        
        if (raiseEvents) OnTextChanged(new CaretPosition(row, 0), new CaretPosition(row, text.Length));
    }
    
    public void RemoveSelectedText() => RemoveRange(Selection.Min, Selection.Max);
    public void RemoveRange(CaretPosition start, CaretPosition end) {
        undoStack.Push(Caret);
        
        if (start > end)
            (end, start) = (start, end);
        
        if (start.Row == end.Row) {
            CurrentLines[start.Row] = CurrentLines[start.Row].Remove(start.Col, end.Col - start.Col);
        } else {
            CurrentLines[start.Row] = CurrentLines[start.Row][..start.Col] + CurrentLines[end.Row][end.Col..];
            CurrentLines.RemoveRange(start.Row + 1, end.Row - start.Row);
        }
        
        OnTextChanged(start, start);
    }
    
    public void RemoveLine(int row, bool raiseEvents = true) {
        if (raiseEvents) undoStack.Push(Caret);
        
        CurrentLines.RemoveAt(row);
        
        if (raiseEvents) OnTextChanged(new CaretPosition(row, 0), new CaretPosition(row, 0));
    }
    
    public void RemoveLines(int min, int max, bool raiseEvents = true) {
        if (raiseEvents) undoStack.Push(Caret);
        
        CurrentLines.RemoveRange(min, max - min + 1);
        
        if (raiseEvents) OnTextChanged(new CaretPosition(min, 0), new CaretPosition(min, 0));
    }

    // public void ReplaceRange(CaretPosition start, CaretPosition end, string text) {
    //     undoStack.Push(Caret);
    //
    //     if (start > end)
    //         (end, start) = (start, end);
    //     
    //     // Remove old text
    //     if (start.Row == end.Row) {
    //         CurrentLines[start.Row] = CurrentLines[start.Row].Remove(start.Col, end.Col - start.Col);
    //     } else {
    //         CurrentLines[start.Row] = CurrentLines[start.Row][..start.Col] + CurrentLines[end.Row][end.Col..];
    //         CurrentLines.RemoveRange(start.Row + 1, end.Row - start.Row);
    //     }
    //     
    //     // Insert new text
    //     var newLines = text.SplitDocumentLines();
    //     if (newLines.Length == 0)
    //         return;
    //     
    //     CurrentLines[start.Row] = CurrentLines[start.Row] + newLines[0];
    //     for (int i = 1; i < newLines.Length - 1; i++)
    //         CurrentLines.Insert(start.Row + i, newLines[i]);
    //     CurrentLines[start.Row + newLines.Length - 1] = newLines[^1] + CurrentLines[start.Row + newLines.Length - 1];
    //     
    //     OnTextChanged();
    // }
    
    public void ReplaceRangeInLine(int row, int startCol, int endCol, string text) {
        undoStack.Push(Caret);

        if (startCol > endCol)
            (endCol, startCol) = (startCol, endCol);
        
        CurrentLines[row] = CurrentLines[row].ReplaceRange(startCol, endCol - startCol, text);
        
        OnTextChanged(new CaretPosition(row, startCol), new CaretPosition(row, startCol + text.Length));
    }
    
    public string GetSelectedText() => GetTextInRange(Selection.Start, Selection.End);
    public string GetTextInRange(CaretPosition start, CaretPosition end) {
        if (start > end)
            (end, start) = (start, end);
        
        if (start.Row == end.Row) {
            return CurrentLines[start.Row][start.Col..end.Col];
        }
        
        var lines = new string[end.Row - start.Row + 1];
        lines[0] = CurrentLines[start.Row][start.Col..];
        for (int i = 1, row = start.Row + 1; row < end.Row; i++, row++)
            lines[i] = CurrentLines[row];
        lines[^1] = CurrentLines[end.Row][..end.Col];
        
        return string.Join(Environment.NewLine, lines);
    }

    #endregion
}