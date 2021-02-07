using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CelesteStudio.RichText {
/// <summary>
/// This class contains the source text (chars and styles).
/// It stores a text lines, the manager of commands, undo/redo stack, styles.
/// </summary>
public class FileTextSource : TextSource, IDisposable {
    readonly List<int> sourceFileLinePositions = new List<int>();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    FileStream _fs;

    Encoding fileEncoding;
    string path;

    // /// <summary>
    // /// Occurs when need to save line in the file
    // /// </summary>
    // public event EventHandler<LinePushedEventArgs> LinePushed;

    public FileTextSource(RichText currentTB)
        : base(currentTB) {
        timer.Interval = 10000;
        timer.Tick += new EventHandler(timer_Tick);
        timer.Enabled = true;
    }

    FileStream fs {
        get {
            try {
                if (_fs == null) {
                    _fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                }

                return _fs;
            } catch (Exception) {
                return null;
            }
        }
    }

    public override Line this[int i] {
        get {
            if (base.lines[i] != null) {
                return lines[i];
            } else {
                for (int j = 0;; j++) {
                    try {
                        LoadLineFromSourceFile(i);
                        CloseFile();
                        break;
                    } catch (IOException) {
                        if (j > 5) {
                            throw;
                        }

                        Thread.Sleep(5);
                    }
                }
            }

            return lines[i];
        }
        set { throw new NotImplementedException(); }
    }

    public override void Dispose() {
        if (fs != null) {
            fs.Dispose();
        }

        timer.Dispose();
    }

    /// <summary>
    /// Occurs when need to display line in the textbox
    /// </summary>
    public event EventHandler<LineNeededEventArgs> LineNeeded;

    void timer_Tick(object sender, EventArgs e) {
        timer.Enabled = false;
        try {
            //UnloadUnusedLines();
        } finally {
            timer.Enabled = true;
        }
    }

    private void UnloadUnusedLines() {
        const int margin = 2000;
        int iStartVisibleLine = CurrentTB.VisibleRange.Start.iLine;
        int iFinishVisibleLine = CurrentTB.VisibleRange.End.iLine;

        int count = 0;
        for (int i = 0; i < Count; i++) {
            if (base.lines[i] != null && !base.lines[i].IsChanged && Math.Abs(i - iFinishVisibleLine) > margin) {
                base.lines[i] = null;
                count++;
            }
        }
    }

    public void OpenFile(string fileName, Encoding enc) {
        Clear();

        CloseFile();

        path = fileName;
        long length = fs.Length;
        //read signature
        enc = DefineEncoding(enc, fs);
        int shift = DefineShift(enc);
        //first line
        sourceFileLinePositions.Add((int) fs.Position);
        base.lines.Add(null);
        //other lines
        while (fs.Position < length) {
            int b = fs.ReadByte();
            if (b == 10) // char \n
            {
                sourceFileLinePositions.Add((int) (fs.Position) + shift);
                base.lines.Add(null);
            }
        }

        Line[] temp = new Line[100];
        int c = base.lines.Count;
        base.lines.AddRange(temp);
        base.lines.TrimExcess();
        base.lines.RemoveRange(c, temp.Length);


        int[] temp2 = new int[100];
        c = base.lines.Count;
        sourceFileLinePositions.AddRange(temp2);
        sourceFileLinePositions.TrimExcess();
        sourceFileLinePositions.RemoveRange(c, temp.Length);


        fileEncoding = enc;

        OnLineInserted(0, Count);
        //load first lines for calc width of the text
        int linesCount = Math.Min(lines.Count, CurrentTB.Height / CurrentTB.CharHeight);
        for (int i = 0; i < linesCount; i++) {
            LoadLineFromSourceFile(i);
        }

        NeedRecalc(new TextChangedEventArgs(0, 1));
        CloseFile();
    }

    private int DefineShift(Encoding enc) {
        if (enc.IsSingleByte) {
            return 0;
        }

        if (enc.HeaderName == "unicodeFFFE") {
            return 0; //UTF16 BE
        }

        if (enc.HeaderName == "utf-16") {
            return 1; //UTF16 LE
        }

        if (enc.HeaderName == "utf-32BE") {
            return 0; //UTF32 BE
        }

        if (enc.HeaderName == "utf-32") {
            return 3; //UTF32 LE
        }

        return 0;
    }

    private static Encoding DefineEncoding(Encoding enc, FileStream fs) {
        int bytesPerSignature = 0;
        byte[] signature = new byte[4];
        int c = fs.Read(signature, 0, 4);
        if (signature[0] == 0xFF && signature[1] == 0xFE && signature[2] == 0x00 && signature[3] == 0x00 && c >= 4) {
            enc = Encoding.UTF32; //UTF32 LE
            bytesPerSignature = 4;
        } else if (signature[0] == 0x00 && signature[1] == 0x00 && signature[2] == 0xFE && signature[3] == 0xFF) {
            enc = new UTF32Encoding(true, true); //UTF32 BE
            bytesPerSignature = 4;
        } else if (signature[0] == 0xEF && signature[1] == 0xBB && signature[2] == 0xBF) {
            enc = Encoding.UTF8; //UTF8
            bytesPerSignature = 3;
        } else if (signature[0] == 0xFE && signature[1] == 0xFF) {
            enc = Encoding.BigEndianUnicode; //UTF16 BE
            bytesPerSignature = 2;
        } else if (signature[0] == 0xFF && signature[1] == 0xFE) {
            enc = Encoding.Unicode; //UTF16 LE
            bytesPerSignature = 2;
        }

        fs.Seek(bytesPerSignature, SeekOrigin.Begin);

        return enc;
    }

    public void CloseFile() {
        if (_fs != null) {
            _fs.Dispose();
        }

        _fs = null;
    }

    public override void ClearIsChanged() {
        foreach (var line in lines) {
            if (line != null) {
                line.IsChanged = false;
            }
        }
    }

    private void LoadLineFromSourceFile(int i) {
        var line = CreateLine();
        fs.Seek(sourceFileLinePositions[i], SeekOrigin.Begin);
        StreamReader sr = new StreamReader(fs, fileEncoding);

        string s = sr.ReadLine();
        if (s == null) {
            s = "";
        }

        //call event handler
        if (LineNeeded != null) {
            var args = new LineNeededEventArgs(s, i);
            LineNeeded(this, args);
            s = args.DisplayedLineText;
            if (s == null) {
                return;
            }
        }

        foreach (char c in s) {
            line.Add(new Char(c));
        }

        base.lines[i] = line;
    }

    public override void InsertLine(int index, Line line) {
        sourceFileLinePositions.Insert(index, -1);
        base.InsertLine(index, line);
    }

    public override void RemoveLine(int index, int count) {
        sourceFileLinePositions.RemoveRange(index, count);
        base.RemoveLine(index, count);
    }

    public override int GetLineLength(int i) {
        if (base.lines[i] == null) {
            return 0;
        } else {
            return base.lines[i].Count;
        }
    }

    public override bool LineHasFoldingStartMarker(int iLine) {
        if (lines[iLine] == null) {
            return false;
        } else {
            return !string.IsNullOrEmpty(lines[iLine].FoldingStartMarker);
        }
    }

    public override bool LineHasFoldingEndMarker(int iLine) {
        if (lines[iLine] == null) {
            return false;
        } else {
            return !string.IsNullOrEmpty(lines[iLine].FoldingEndMarker);
        }
    }

    internal void UnloadLine(int iLine) {
        //if (lines[iLine] != null && !lines[iLine].IsChanged)
        //	lines[iLine] = null;
    }
}

public class LineNeededEventArgs : EventArgs {
    public LineNeededEventArgs(string sourceLineText, int displayedLineIndex) {
        this.SourceLineText = sourceLineText;
        this.DisplayedLineIndex = displayedLineIndex;
        this.DisplayedLineText = sourceLineText;
    }

    public string SourceLineText { get; private set; }
    public int DisplayedLineIndex { get; private set; }

    /// <summary>
    /// This text will be displayed in textbox
    /// </summary>
    public string DisplayedLineText { get; set; }
}

public class LinePushedEventArgs : EventArgs {
    public LinePushedEventArgs(string sourceLineText, int displayedLineIndex, string displayedLineText) {
        this.SourceLineText = sourceLineText;
        this.DisplayedLineIndex = displayedLineIndex;
        this.DisplayedLineText = displayedLineText;
        this.SavedText = displayedLineText;
    }

    public string SourceLineText { get; private set; }
    public int DisplayedLineIndex { get; private set; }

    /// <summary>
    /// This property contains only changed text.
    /// If text of line is not changed, this property contains null.
    /// </summary>
    public string DisplayedLineText { get; private set; }

    /// <summary>
    /// This text will be saved in the file
    /// </summary>
    public string SavedText { get; set; }
}
}