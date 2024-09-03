using CelesteStudio.Data;

namespace CelesteStudio.Editing.ContextActions;

public class OpenReadFile : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_OpenReadFile;

    public override PopupMenu.Entry? Check() {
        if (Editor.GetOpenReadFileLink(Document.Caret.Row) is { } lineLink) {
            return CreateEntry("", () => lineLink());
        }
        
        return null;
    }
}

public class GotoPlayLine : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_GoToPlayLine;
    
    public override PopupMenu.Entry? Check() {
        if (Editor.GetGotoPlayLineLink(Document.Caret.Row) is { } lineLink) {
            return CreateEntry("", () => lineLink());
        }
        
        return null;
    }
}