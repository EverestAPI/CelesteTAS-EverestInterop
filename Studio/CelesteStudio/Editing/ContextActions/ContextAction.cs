using System;

namespace CelesteStudio.Editing.ContextActions;

public abstract class ContextAction {
    protected static Editor Editor => Studio.Instance.Editor;
    protected static Document Document => Studio.Instance.Editor.Document;

    public abstract string Name { get; }

    public abstract PopupMenu.Entry? Check();

    protected PopupMenu.Entry CreateEntry(string extraText, Action onUse) {
        return new PopupMenu.Entry {
            SearchText = Name,
            DisplayText = Name,
            ExtraText = extraText,
            OnUse = () => {
                onUse();
                Editor.ActivePopupMenu = null;
            },
        };
    }
    
}