using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CelesteStudio.Util;

namespace CelesteStudio;

public struct CaretPosition(int row = 0, int col = 0) {
    public int Row = row, Col = col;
    
    public static bool operator ==(CaretPosition lhs, CaretPosition rhs) => lhs.Row == rhs.Row && lhs.Col == rhs.Col;
    public static bool operator !=(CaretPosition lhs, CaretPosition rhs) => !(lhs == rhs);
    public static bool operator >(CaretPosition lhs, CaretPosition rhs) => lhs.Row > rhs.Row || (lhs.Row == rhs.Row && lhs.Col > rhs.Col);
    public static bool operator <(CaretPosition lhs, CaretPosition rhs) => lhs.Row < rhs.Row || (lhs.Row == rhs.Row && lhs.Col < rhs.Col);
    
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
    // Should only be used while an actual document is being loaded
    public static readonly Document Dummy = new(string.Empty);
    
    public CaretPosition Caret = new();
    public Selection Selection = new();
    
    public string FilePath { get; set; }
    public string FileName => FilePath == null ? null : Path.GetFileName(FilePath);

    private readonly UndoStack undoStack = new();

    private List<string> CurrentLines => undoStack.Stack[undoStack.Curr].Lines;
    public IReadOnlyList<string> Lines => CurrentLines.AsReadOnly();
    
    public string Text => string.Join(Environment.NewLine, CurrentLines);

    public bool Dirty { get; private set; }
    
    public event Action<Document> TextChanged = doc => doc.Dirty = true;

    private Document(string contents) {
        undoStack.Stack[undoStack.Curr] = new UndoStack.Entry(contents.Split('\n', '\r').ToList(), Caret);
        
        Studio.CelesteService.Server.LinesUpdated += OnLinesUpdated;
    }

    ~Document() {
        Studio.CelesteService.Server.LinesUpdated -= OnLinesUpdated;
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
        } catch (Exception e) {
            Console.WriteLine(e);
        }
    }

    private void OnLinesUpdated(Dictionary<int, string> newLines) {
        foreach ((int lineNum, string newText) in newLines) {
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
    
    public void Insert(string text) => Caret = Insert(Caret, text);
    public CaretPosition Insert(CaretPosition pos, string text) {
        var newLines = text.Split('\n', '\r');
        if (newLines.Length == 0)
            return pos;
        
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
        
        TextChanged.Invoke(this);
        return pos;
    }
    
    public void InsertLineAbove(string text) => InsertNewLine(Caret.Row, text);
    public void InsertLineBelow(string text) => InsertNewLine(Caret.Row + 1, text);
    public void InsertNewLine(int line, string text) {
        var newLines = text.Split('\n', '\r');
        if (newLines.Length == 0)
            return;
        
        undoStack.Push(Caret);
        
        CurrentLines.InsertRange(line, newLines);
        
        TextChanged.Invoke(this);
    }
    
    public void ReplaceLine(int row, string text) {
        undoStack.Push(Caret);
        
        CurrentLines[row] = text;
        
        TextChanged.Invoke(this);
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
        
        TextChanged.Invoke(this);
    }

    public void ReplaceRange(CaretPosition start, CaretPosition end, string text) {
        undoStack.Push(Caret);

        if (start > end)
            (end, start) = (start, end);
        
        // Remove old text
        if (start.Row == end.Row) {
            CurrentLines[start.Row] = CurrentLines[start.Row].Remove(start.Col, end.Col - start.Col);
        } else {
            CurrentLines[start.Row] = CurrentLines[start.Row][..start.Col] + CurrentLines[end.Row][end.Col..];
            CurrentLines.RemoveRange(start.Row + 1, end.Row - start.Row);
        }
        
        // Insert new text
        var newLines = text.Split('\n', '\r');
        if (newLines.Length == 0)
            return;
        
        CurrentLines[start.Row] = CurrentLines[start.Row] + newLines[0];
        for (int i = 1; i < newLines.Length - 1; i++)
            CurrentLines.Insert(start.Row + i, newLines[i]);
        CurrentLines[start.Row + newLines.Length - 1] = newLines[^1] + CurrentLines[start.Row + newLines.Length - 1];
        
        TextChanged.Invoke(this);
    }
    
    public void ReplaceRangeInLine(int line, int startCol, int endCol, string text) {
        undoStack.Push(Caret);

        if (startCol > endCol)
            (endCol, startCol) = (startCol, endCol);
        
        CurrentLines[line] = CurrentLines[line].ReplaceRange(startCol, endCol - startCol, text);
        
        TextChanged.Invoke(this);
    }

    #endregion
}