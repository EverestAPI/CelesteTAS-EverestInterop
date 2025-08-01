using System;
using Eto.Forms;
using System.Collections.Generic;

namespace CelesteStudio.Editing.ContextActions;

public abstract class ContextAction {
    protected static Editor Editor => Studio.Instance.Editor;
    protected static Document Document => Studio.Instance.Editor.Document;

    public abstract string Identifier { get; }
    public abstract string DisplayName { get; }
    public abstract Hotkey DefaultHotkey { get; }

    public abstract PopupMenu.Entry? Check();

    protected PopupMenu.Entry CreateEntry(string extraText, Action onUse) {
        var hotkey = Settings.Instance.KeyBindings.GetValueOrDefault(Identifier, DefaultHotkey);

        return new PopupMenu.Entry {
            SearchText = DisplayName,
            DisplayText = DisplayName,
            ExtraText = hotkey.KeysOrNone != Keys.None ? hotkey.KeysOrNone.ToShortcutString() : extraText,
            OnUse = () => {
                onUse();
                Editor.ActivePopupMenu = null;
            },
        };
    }

    public ActionBinding ToBinding() {
        return new ActionBinding(Identifier, DisplayName, Binding.Category.ContextActions, DefaultHotkey, () => {
            if (Check() is { } action) {
                action.OnUse();
            }
        });
    }
}
