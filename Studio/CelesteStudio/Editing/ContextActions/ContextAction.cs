using System;

namespace CelesteStudio.Editing.ContextActions;

public abstract class ContextAction {
    protected Editor Editor => Studio.Instance.Editor;
    protected Document Document => Studio.Instance.Editor.Document;

    public abstract string Name { get; }

    public abstract AutoCompleteMenu.Entry? Check();

    protected AutoCompleteMenu.Entry CreateEntry(string extraText, Action onUse) {
        return new AutoCompleteMenu.Entry {
            SearchText = Name,
            DisplayText = Name,
            ExtraText = extraText,
            OnUse = () => {
                onUse();
                Editor.contextActionsMenu.Visible = false;
            },
        };
    }
    
}