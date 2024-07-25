    using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto;

namespace CelesteStudio.Editing;

public struct CaretPosition(int row = 0, int col = 0) {
    public int Row = row, Col = col;
    
    public static bool operator ==(CaretPosition lhs, CaretPosition rhs) => lhs.Row == rhs.Row && lhs.Col == rhs.Col;
    public static bool operator !=(CaretPosition lhs, CaretPosition rhs) => !(lhs == rhs);
    public static bool operator >(CaretPosition lhs, CaretPosition rhs) => lhs.Row > rhs.Row || (lhs.Row == rhs.Row && lhs.Col > rhs.Col);
    public static bool operator <(CaretPosition lhs, CaretPosition rhs) => lhs.Row < rhs.Row || (lhs.Row == rhs.Row && lhs.Col < rhs.Col);
    public static bool operator >=(CaretPosition lhs, CaretPosition rhs) => lhs > rhs || lhs == rhs;
    public static bool operator <=(CaretPosition lhs, CaretPosition rhs) => lhs < rhs || lhs == rhs;
    
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
    // Labels include #'s with no space and breakpoints
    LabelUp,
    LabelDown,
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
        if (Start > End) {
            (Start, End) = (End, Start);
        }
    }
}

public class Anchor {
    public int Row;
    public int MinCol, MaxCol;
    
    public object? UserData;
    public Action? OnRemoved;
    
    public bool IsPositionInside(CaretPosition position) => position.Row == Row && position.Col >= MinCol && position.Col <= MaxCol;
    public Anchor Clone() => new() { Row = Row, MinCol = MinCol, MaxCol = MaxCol, UserData = UserData, OnRemoved = OnRemoved };
}

public class UndoStack(int stackSize = 256) {
    public struct Entry(List<string> lines, Dictionary<int, List<Anchor>> anchors, CaretPosition caret) {
        public readonly List<string> Lines = lines;
        public readonly Dictionary<int, List<Anchor>> Anchors = anchors;
        public CaretPosition Caret = caret;
    }
    
    public int Curr = 0;
    public readonly Entry[] Stack = new Entry[stackSize];
    
    private int head = 0, tail = 0;
    
    public void Push(CaretPosition caret) {
        head = (Curr + 1).Mod(stackSize);
        if (head == tail)
            tail = (tail + 1).Mod(stackSize); // Discard the oldest entry
        
        Stack[Curr].Caret = caret;
        Stack[head] = new Entry(
            // Make sure to copy lines / anchors
            [..Stack[Curr].Lines],
            Stack[Curr].Anchors.ToDictionary(entry => entry.Key, entry => entry.Value.Select(anchor => anchor.Clone()).ToList()),
            // Will be overwritten later, so it doesn't matter what's used
            caret 
        );
        Curr = head;
    }
    
    public void Undo() {
        if (Curr == tail) return;
        Curr = (Curr - 1).Mod(stackSize);
    }
    public void Redo() {
        if (Curr == head) return;
        Curr = (Curr + 1).Mod(stackSize);
    }
}

public class Document {
    // Unify all TASes to use a single line separator
    public const char NewLine = '\n';
    
    // Used while the document isn't saved yet
    public static string ScratchFile => Path.Combine(Settings.BaseConfigPath, ".temp.tas"); 
    
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

            // Bit-cast to uint to avoid negative numbers
            uint hash;
            unchecked { hash = (uint)FilePath.GetStableHashCode(); }

            string backupSubDir = isBackupFile 
                ? Directory.GetParent(FilePath)!.FullName 
                : $"{FileName}_{hash}";
            
            return Path.Combine(backupBaseDir, backupSubDir);
        }
    }

    private readonly UndoStack undoStack = new();

    private List<string> CurrentLines => undoStack.Stack[undoStack.Curr].Lines;
    public IReadOnlyList<string> Lines => CurrentLines.AsReadOnly();

    // An anchor is a part of the document, which will move with the text its placed on.
    // They can hold arbitrary user data.
    // As their text gets edited, they will grow / shrink in size or removed entirely.
    private Dictionary<int, List<Anchor>> CurrentAnchors => undoStack.Stack[undoStack.Curr].Anchors;
    public IEnumerable<Anchor> Anchors => CurrentAnchors.SelectMany(pair => pair.Value);
    
    public string Text => string.Join(NewLine, CurrentLines);
    public bool Dirty { get; private set; }
    
    private QueuedUpdate? queuedUpdate = null;
    
    // NOTE: The min/max rows may contain more than what was actually changed
    public event Action<Document, int, int> TextChanged = (doc, _, _) => {
        if (Settings.Instance.AutoSave) {
            doc.Save();
            return;
        }
        
        doc.Dirty = true;
    };
    private void OnTextChanged(int minRow, int maxRow) => TextChanged.Invoke(this, minRow, maxRow);
    
    private Document(string contents) {
        contents = contents.ReplaceLineEndings(NewLine.ToString());
        undoStack.Stack[undoStack.Curr] = new UndoStack.Entry(contents.SplitDocumentLines().ToList(), [], Caret);
        
        if (!string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath)) {
            // Save with the new line endings
            Save();    
        }
        
        CommunicationWrapper.LinesUpdated += OnLinesUpdated;
    }

    ~Document() {
        CommunicationWrapper.LinesUpdated -= OnLinesUpdated;
    }
    
    public void Dispose() {
        CommunicationWrapper.LinesUpdated -= OnLinesUpdated;
    }

    public static Document? Load(string path) {
        try {
            string text = File.ReadAllText(path);
            return new Document(text) {
                FilePath = path,
            };
        } catch (Exception e) {
            Console.Error.WriteLine(e);
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
            Console.Error.WriteLine(e);
        }
    }
    
    private void CreateBackup() {
        var backupDir = BackupDirectory;
        if (!Directory.Exists(backupDir))
            Directory.CreateDirectory(backupDir);
        
        string[] files = Directory.GetFiles(backupDir)
            // Sort for oldest first
            .OrderBy(file => File.GetLastWriteTimeUtc(file).Ticks)
            .ToArray();
        
        if (files.Length > 0) {
            var lastFileTime = File.GetLastWriteTimeUtc(files.Last());
            
            // Wait until next interval
            if (Settings.Instance.AutoBackupRate > 0 && lastFileTime.AddMinutes(Settings.Instance.AutoBackupRate) >= DateTime.UtcNow) {
                return;
            }
            
            // Delete the oldest backups until the desired count is reached
            if (Settings.Instance.AutoBackupCount > 0 && files.Length >= Settings.Instance.AutoBackupCount) {
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
            // Cannot properly update anchors
            CurrentAnchors.Remove(lineNum);
        }
    }
    
    #region Anchors
    
    public void AddAnchor(Anchor anchor) {
        CurrentAnchors.TryAdd(anchor.Row, []);
        CurrentAnchors[anchor.Row].Add(anchor);
    }
    public void RemoveAnchor(Anchor anchor) {
        foreach ((int _, List<Anchor> list) in CurrentAnchors) {
            if (list.Remove(anchor)) {
                break;
            }
        }
    }
    public void RemoveAnchorsIf(Predicate<Anchor> predicate) {
        foreach ((int _, List<Anchor> list) in CurrentAnchors) {
            list.RemoveAll(predicate);
        }
    }
    public Anchor? FindFirstAnchor(Func<Anchor, bool> predicate) {
        foreach ((int _, List<Anchor> list) in CurrentAnchors) {
            if (list.FirstOrDefault(predicate) is { } anchor) {
                return anchor;
            }
        }
        return null;
    }
    public IEnumerable<Anchor> FindAnchors(Func<Anchor, bool> predicate) {
        foreach ((int _, List<Anchor> list) in CurrentAnchors) {
            foreach (var anchor in list) {
                if (predicate(anchor)) {
                    yield return anchor;
                }
            }
        }
    }
    
    #endregion

    #region Text Manipulation
    
    public sealed class QueuedUpdate(Document document, bool raiseEvents) : IDisposable {
        private int currMinRow = -1, currMaxRow = -1;
        
        public void PushChange(int minRow, int maxRow) {
            if (!raiseEvents) {
                return;
            }
            
            if (currMinRow == -1 || currMaxRow == -1) {
                currMinRow = minRow;
                currMaxRow = maxRow;
            } else {
                currMinRow = Math.Min(currMinRow, minRow);
                currMaxRow = Math.Max(currMaxRow, maxRow);
            }
        }
        public void Dispose() {
            document.queuedUpdate = null;
            if (!raiseEvents || currMinRow == -1 || currMaxRow == -1) {
                return;
            }
            
            document.OnTextChanged(Math.Max(0, currMinRow), Math.Min(document.Lines.Count, currMaxRow));
        }
    }
    
    // Start a new update, if there isn't one active already
    public QueuedUpdate Update(bool raiseEvents = true) {
        if (raiseEvents) {
            undoStack.Push(Caret);
        }
        
        return queuedUpdate ??= new QueuedUpdate(this, raiseEvents);
    }
    
    private void PushUndoState() {
        if (queuedUpdate != null)
            return;
        
        Console.WriteLine("WARNING: Updated without Document.Update()");
        undoStack.Push(Caret);
    }
    private void ChangedText(int minRow, int maxRow) {
        if (minRow > maxRow)
            (minRow, maxRow) = (maxRow, minRow);
        
        if (queuedUpdate != null)
            queuedUpdate.PushChange(minRow, maxRow);
        else
            OnTextChanged(minRow, maxRow);
    }
    
    public void Undo() {
        undoStack.Undo();
        Caret = undoStack.Stack[undoStack.Curr].Caret;
        
        OnTextChanged(0, CurrentLines.Count - 1);
    }
    public void Redo() {
        undoStack.Redo();
        Caret = undoStack.Stack[undoStack.Curr].Caret;
        
        OnTextChanged(0, CurrentLines.Count - 1);
    }
    
    public void Insert(string text) => Caret = Insert(Caret, text);
    public CaretPosition Insert(CaretPosition pos, string text) {
        var newLines = text.ReplaceLineEndings(NewLine.ToString()).SplitDocumentLines();
        if (newLines.Length == 0)
            return pos;
        
        var oldPos = pos;
        PushUndoState();

        if (newLines.Length == 1) {
            // Update anchors
            if (CurrentAnchors.TryGetValue(pos.Row, out var anchors)) {
                foreach (var anchor in anchors) {
                    if (pos.Col < anchor.MinCol) {
                        anchor.MinCol += text.Length;
                    }
                    if (pos.Col <= anchor.MaxCol) {
                        anchor.MaxCol += text.Length;
                    }
                }
            }
            
            CurrentLines[pos.Row] = CurrentLines[pos.Row].Insert(pos.Col, text);
            pos.Col += text.Length;
        } else {
            // Move anchors below down
            for (int row = CurrentLines.Count - 1; row > pos.Row; row--) {
                if (CurrentAnchors.Remove(row, out var aboveAnchors)) {
                    CurrentAnchors[row + newLines.Length - 1] = aboveAnchors;
                    foreach (var anchor in aboveAnchors) {
                        anchor.Row += newLines.Length - 1;
                    }
                }
            }
            // Update anchors
            if (CurrentAnchors.TryGetValue(pos.Row, out var anchors)) {
                int newRow = pos.Row + newLines.Length - 1;
                
                CurrentAnchors.TryAdd(newRow, []);
                var newAnchors = CurrentAnchors[newRow];
                
                for (int i = anchors.Count - 1; i >= 0; i = Math.Min(i - 1, anchors.Count - 1)) {
                    var anchor = anchors[i];

                    // Invalidate in between
                    if (pos.Col >= anchor.MinCol && pos.Col <= anchor.MaxCol) {
                        anchor.OnRemoved?.Invoke();
                        anchors.Remove(anchor);
                        continue;
                    }
                    if (pos.Col >= anchor.MinCol) {
                        continue;
                    }
                    
                    int offset = anchor.MinCol - pos.Col;
                    int len = anchor.MaxCol - anchor.MinCol;
                    anchor.MinCol = offset + newLines[0].Length;
                    anchor.MaxCol = offset + len + newLines[0].Length;
                    anchor.Row = newRow;
                    anchors.Remove(anchor);
                    newAnchors.Add(anchor);
                }
            }
            
            string left  = CurrentLines[pos.Row][..pos.Col];
            string right = CurrentLines[pos.Row][pos.Col..];
        
            CurrentLines[pos.Row] = left + newLines[0];
            for (int i = 1; i < newLines.Length; i++) {
                CurrentLines.Insert(pos.Row + i, newLines[i]);
            }
            pos.Row += newLines.Length - 1;
            pos.Col = newLines[^1].Length;

            CurrentLines[pos.Row] += right;
        }
        
        ChangedText(oldPos.Row, pos.Row);
        
        return pos;
    }
    
    public void InsertLineAbove(string text) => InsertLine(Caret.Row, text);
    public void InsertLineBelow(string text) => InsertLine(Caret.Row + 1, text);
    public void InsertLine(int row, string text) {
        PushUndoState();
        
        var newLines = text.SplitDocumentLines();
        if (newLines.Length == 0)
            CurrentLines.Insert(row, string.Empty);
        else
            CurrentLines.InsertRange(row, newLines);
        
        int newLineCount = text.Count(c => c == NewLine) + 1;
        
        if (Caret.Row >= row)
            Caret.Row += newLineCount;
        
        ChangedText(row, row + newLineCount);
    }
    
    public void ReplaceLine(int row, string text) {
        var newLines = text.SplitDocumentLines();
        ReplaceLines(row, newLines);
    }
    
    public void ReplaceLines(int row, string[] newLines) {
        PushUndoState();
        
        if (newLines.Length == 0) {
            CurrentLines[row] = string.Empty;
        } else if (newLines.Length == 1) {
            CurrentLines[row] = newLines[0];
        } else {
            CurrentLines[row] = newLines[0];
            CurrentLines.InsertRange(row + 1, newLines[1..]);
        }

        int newLineCount = newLines.Length > 0 ? newLines.Length-1 : 0;
        
        if (Caret.Row >= row)
            Caret.Row += newLineCount;
        
        ChangedText(row, row + newLineCount);
    }
    
    public void SwapLines(int rowA, int rowB) {
        PushUndoState();
        
        (CurrentLines[rowA], CurrentLines[rowB]) = (CurrentLines[rowB], CurrentLines[rowA]); 
        
        ChangedText(rowA, rowB);
    }
    
    public void RemoveRange(CaretPosition start, CaretPosition end) {
        if (start.Row == end.Row) {
            RemoveRangeInLine(start.Row, start.Col, end.Col);
            return;
        }
        
        PushUndoState();
        
        if (start > end)
            (end, start) = (start, end);
        
        List<Anchor>? anchors;
        // Invalidate in between
        for (int row = start.Row; row <= end.Row; row++) {
            if (CurrentAnchors.TryGetValue(row, out anchors)) {
                for (int i = anchors.Count - 1; i >= 0; i = Math.Min(i - 1, anchors.Count - 1)) {
                    var anchor = anchors[i];
                    
                    if (row == start.Row && anchor.MaxCol <= start.Col ||
                        row == end.Row && anchor.MinCol <= end.Col) 
                    {
                        continue;
                    }
                    
                    anchor.OnRemoved?.Invoke();
                    anchors.Remove(anchor);
                }
            }
        }
        // Update anchors
        if (CurrentAnchors.TryGetValue(end.Row, out anchors)) {
            CurrentAnchors.TryAdd(start.Row, []);
            var newAnchors = CurrentAnchors[start.Row];
            
            for (int i = anchors.Count - 1; i >= 0; i = Math.Min(i - 1, anchors.Count - 1)) {
                var anchor = anchors[i];
                
                int offset = anchor.MinCol - end.Col;
                int len = anchor.MaxCol - anchor.MinCol;
                anchor.MinCol = offset + start.Col;
                anchor.MaxCol = offset + len + start.Col;
                anchor.Row = start.Row;
                anchors.Remove(anchor);
                newAnchors.Add(anchor);
            }
        }
        // Move anchors below up
        for (int row = end.Row + 1; row < CurrentLines.Count; row++) {
            if (CurrentAnchors.Remove(row, out var aboveAnchors)) {
                CurrentAnchors[row - (end.Row - start.Row)] = aboveAnchors;
                foreach (var anchor in aboveAnchors) {
                    anchor.Row -= end.Row - start.Row;
                }
            }
        }
        
        CurrentLines[start.Row] = CurrentLines[start.Row][..start.Col] + CurrentLines[end.Row][end.Col..];
        CurrentLines.RemoveRange(start.Row + 1, end.Row - start.Row);
        
        ChangedText(start.Row, start.Row);
    }
    
    public void RemoveRangeInLine(int row, int startCol, int endCol) {
        PushUndoState();
        
        if (startCol > endCol)
            (endCol, startCol) = (startCol, endCol);
        
        // Update anchors
        if (CurrentAnchors.TryGetValue(row, out var anchors)) {
            for (int i = anchors.Count - 1; i >= 0; i = Math.Min(i - 1, anchors.Count - 1)) {
                var anchor = anchors[i];
                
                // Invalidate when range partially intersects
                if (startCol < anchor.MinCol && endCol > anchor.MinCol ||
                    startCol < anchor.MaxCol && endCol > anchor.MaxCol ||
                    // Remove entirely when it's 0 wide
                    anchor.MinCol == anchor.MaxCol && startCol <= anchor.MinCol && endCol >= anchor.MaxCol)
                {
                    anchor.OnRemoved?.Invoke();
                    anchors.Remove(anchor);
                }
                
                if (endCol <= anchor.MinCol) {
                    anchor.MinCol -= endCol - startCol;
                }
                if (endCol <= anchor.MaxCol) {
                    anchor.MaxCol -= endCol - startCol;
                }
            }
        }
        
        CurrentLines[row] = CurrentLines[row].Remove(startCol, endCol - startCol);
        
        ChangedText(row, row);
    }
    
    public void RemoveLine(int row) {
        PushUndoState();
        
        CurrentLines.RemoveAt(row);
        
        ChangedText(row, row);
    }
    
    /// Removes an inclusive range of lines from min..max
    public void RemoveLines(int min, int max) {
        PushUndoState();
        
        CurrentLines.RemoveRange(min, max - min + 1);
        
        ChangedText(min, max);
    }
    
    public void ReplaceRangeInLine(int row, int startCol, int endCol, string text) {
        PushUndoState();

        if (startCol > endCol)
            (endCol, startCol) = (startCol, endCol);
        
        // Update anchors
        if (CurrentAnchors.TryGetValue(row, out var anchors)) {
            for (int i = anchors.Count - 1; i >= 0; i = Math.Min(i - 1, anchors.Count - 1)) {
                var anchor = anchors[i];
                
                // Invalidate when range partially intersects
                if (startCol < anchor.MinCol && endCol > anchor.MinCol ||
                    startCol < anchor.MaxCol && endCol > anchor.MaxCol)
                {
                    anchor.OnRemoved?.Invoke();
                    anchors.Remove(anchor);
                }
                
                if (anchor.MinCol == anchor.MaxCol) {
                    // Paste the new text into the anchor
                    anchor.MaxCol += text.Length;
                } else {
                    if (endCol <= anchor.MinCol) {
                        anchor.MinCol -= endCol - startCol;
                        anchor.MinCol += text.Length;
                    }
                    if (endCol <= anchor.MaxCol) {
                        anchor.MaxCol -= endCol - startCol;
                        anchor.MaxCol += text.Length;
                    }
                }
            }
        }
        
        CurrentLines[row] = CurrentLines[row].ReplaceRange(startCol, endCol - startCol, text);
        
        ChangedText(row, row);
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