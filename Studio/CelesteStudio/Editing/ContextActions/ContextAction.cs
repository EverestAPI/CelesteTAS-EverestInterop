using System;
using CelesteStudio.Data;
using Eto.Forms;

namespace CelesteStudio.Editing.ContextActions;

public abstract class ContextAction {
    protected static Editor Editor => Studio.Instance.Editor;
    protected static Document Document => Studio.Instance.Editor.Document;

    public abstract MenuEntry Entry { get; }

    public abstract PopupMenu.Entry? Check();

    protected PopupMenu.Entry CreateEntry(string extraText, Action onUse) {
        return new PopupMenu.Entry {
            SearchText = Entry.GetName(),
            DisplayText = Entry.GetName(),
            ExtraText = Entry.GetHotkey() != Keys.None ? Entry.GetHotkey().ToShortcutString() : extraText,
            OnUse = () => {
                onUse();
                Editor.ActivePopupMenu = null;
            },
        };
    }
    
}