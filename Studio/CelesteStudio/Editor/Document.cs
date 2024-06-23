using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CelesteStudio.Util;

namespace CelesteStudio;

public struct CaretPosition(int row = 0, int col = 0) {
    public int Row = row, Col = col;
    
    public static bool operator==(CaretPosition lhs, CaretPosition rhs) => lhs.Row == rhs.Row && lhs.Col == rhs.Col;
    public static bool operator !=(CaretPosition lhs, CaretPosition rhs) => !(lhs == rhs);
    
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
    public bool Empty => Start == End;
}

public class Document {
    //private const string EmptyDocument = "RecordCount: 1\n\n#Start\n";
    private const string EmptyDocument = "";

    public CaretPosition Caret = new();
    public Selection Selection = new();
    
    public string? FilePath { get; set; }
    public string? FileName => FilePath == null ? null : Path.GetFileName(FilePath);

    private readonly List<string> lines;
    public IReadOnlyList<string> Lines => lines.AsReadOnly();
    
    public string Text => string.Join(Environment.NewLine, lines);

    public bool Dirty { get; private set; }
    
    public event Action<Document> TextChanged = doc => doc.Dirty = true;

    private Document(string contents) {
        lines = contents.Split('\n', '\r').ToList();
        
        Studio.CelesteService.Server.LinesUpdated += OnLinesUpdated;
    }

    ~Document() {
        Studio.CelesteService.Server.LinesUpdated -= OnLinesUpdated;
    }

    public static Document CreateBlank() => new(EmptyDocument);

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
        if (FilePath != null) {
            try {
                File.WriteAllText(FilePath, Text);
                Dirty = false;
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        }
    }

    private void OnLinesUpdated(Dictionary<int, string> newLines) {
        foreach ((int lineNum, string newText) in newLines) {
            lines[lineNum] = newText;
        }
    }

    #region Text Manipulation Helpers

    public void Insert(string text) => Caret = Insert(Caret, text);
    public CaretPosition Insert(CaretPosition pos, string text) {
        var newLines = text.Split('\n', '\r');
        if (newLines.Length == 0)
            return pos;

        if (newLines.Length == 1) {
            lines[pos.Row] = lines[pos.Row].Insert(pos.Col, text);
            pos.Col += text.Length;
        } else {
            string left  = lines[pos.Row][..pos.Col];
            string right = lines[pos.Row][pos.Col..];
        
            lines[pos.Row] = left + newLines[0];
            for (int i = 1; i < newLines.Length; i++)
                lines.Insert(pos.Row + i, newLines[i]);
            pos.Row += newLines.Length - 1;
            pos.Col = newLines[^1].Length;
            lines[pos.Row] += right;
        }
        
        TextChanged.Invoke(this);
        return pos;
    }
    public void InsertNewLine(int line, string text) {
        lines.Insert(line, text);
        
        TextChanged.Invoke(this);
    }
    
    public void ReplaceLine(int row, string text) {
        lines[row] = text;
        
        TextChanged.Invoke(this);
    }
    
    public void RemoveSelectedText() => RemoveRange(Selection.Start, Selection.End);
    public void RemoveRange(CaretPosition start, CaretPosition end) {
        lines[start.Row] = lines[start.Row][..start.Col];
        lines[end.Row] = lines[end.Row][end.Col..];
        for (int i = start.Row + 1; i < end.Row; i++)
            lines.RemoveAt(i);
        
        TextChanged.Invoke(this);
    }

    public void ReplaceRange(CaretPosition start, CaretPosition end, string text) {
        // Remove old text
        RemoveRange(start, end);
        
        // Insert new text
        var newLines = text.Split('\n', '\r');
        if (newLines.Length == 0)
            return;
        
        lines[start.Row] = lines[start.Row] + newLines[0];
        for (int i = 1; i < newLines.Length - 1; i++)
            lines.Insert(start.Row + i, newLines[i]);
        lines[start.Row + newLines.Length - 1] = newLines[^1] + lines[start.Row + newLines.Length - 1];
        
        TextChanged.Invoke(this);
    }
    
    public void ReplaceRangeInLine(int line, int startCol, int endCol, string text) {
        if (startCol > endCol)
            (endCol, startCol) = (startCol, endCol);
        
        lines[line] = lines[line].ReplaceRange(startCol, endCol - startCol, text);
        
        TextChanged.Invoke(this);
    }

    #endregion
}