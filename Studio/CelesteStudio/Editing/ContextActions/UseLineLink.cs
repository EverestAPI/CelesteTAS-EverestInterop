using System;
using CelesteStudio.Data;

namespace CelesteStudio.Editing.ContextActions;

public class OpenReadFile : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_OpenReadFile;

    public override PopupMenu.Entry? Check() {
        string currentLine = Document.Lines[Document.Caret.Row];
        
        if (!CommandLine.TryParse(currentLine, out var commandLine) || !commandLine.IsCommand("Read") || Editor.GetLineLink(Document.Caret.Row) is not { } lineLink) {
            return null;
        }
        
        return CreateEntry("", () => lineLink());
    }
}

public class GotoPlayLine : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_GoToPlayLine;
    
    public override PopupMenu.Entry? Check() {
        string currentLine = Document.Lines[Document.Caret.Row];
        
        if (!CommandLine.TryParse(currentLine, out var commandLine) || !commandLine.IsCommand("Play") || Editor.GetLineLink(Document.Caret.Row) is not { } lineLink) {
            return null;
        }
        
        return CreateEntry("", () => lineLink());
    }
}