using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CelesteStudio;

public class Document {
    public struct CaretPosition(int row = 0, int col = 0)
    {
        public int Row = row, Col = col;
    }
    
    private const string EmptyDocument = "RecordCount: 1\n\n#Start\n";

    public CaretPosition Caret = new();
    
    public string? FilePath { get; set; }
    public string? FileName => FilePath == null ? null : Path.GetFileName(FilePath);

    private readonly List<string> lines = [];
    public IReadOnlyList<string> Lines => lines.AsReadOnly();
    
    public string Text => string.Join(Environment.NewLine, lines);

    public bool Dirty { get; private set; }
    
    public event Action TextChanged = () => {};

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

    public void Insert(string text) => Insert(Caret, text);
    public void Insert(CaretPosition pos, string text)
    {
        lines[Caret.Row] = lines[Caret.Row].Insert(Caret.Col, text);
        Caret.Col += text.Length;
    }

    #endregion
}