using System;
using CelesteStudio.Data;

namespace CelesteStudio.Editing.ContextActions;

public class CombineConsecutiveSameInputs : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_CombineConsecutiveSameInputs;
    
    public override PopupMenu.Entry? Check() {
        if (Document.Selection.Empty && !ActionLine.TryParse(Document.Lines[Document.Caret.Row], out _)) {
            // No merge target
            return null;
        } else if (!Document.Selection.Empty) {
            for (int row = Document.Selection.Min.Row; row <= Document.Selection.Max.Row; row++) {
                if (ActionLine.TryParse(Document.Lines[row], out _)) {
                    goto BreakLoop;
                }
            }
            // No action-lines inside selection
            return null;
            
            BreakLoop:;
        }
        
        return CreateEntry("", () => Editor.CombineInputs(sameActions: true));
    }
}

public class ForceCombineInputFrames : ContextAction {
    public override MenuEntry Entry => MenuEntry.ContextActions_ForceCombineInputFrames;

    public override PopupMenu.Entry? Check() {
        if (Document.Selection.Empty) {
            // Can't force merge without a selection
            return null;
        }
        
        return CreateEntry("", () => Editor.CombineInputs(sameActions: false));
    }
}