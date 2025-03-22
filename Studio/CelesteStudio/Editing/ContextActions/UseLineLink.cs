using System;

namespace CelesteStudio.Editing.ContextActions;

public class UseLineLink(Editor.LineLinkType type) : ContextAction {
    public override string Identifier => type switch {
        Editor.LineLinkType.OpenReadFile => "ContextActions_OpenReadFile",
        Editor.LineLinkType.GoToPlayLine => "ContextActions_GoToPlayLine",
        _ => throw new ArgumentOutOfRangeException()
    };
    public override string DisplayName => type switch {
        Editor.LineLinkType.OpenReadFile => "Open \"Read\" File",
        Editor.LineLinkType.GoToPlayLine => "Go To \"Play\" Line",
        _ => throw new ArgumentOutOfRangeException()
    };
    public override Hotkey DefaultHotkey => Hotkey.None;

    public override PopupMenu.Entry? Check() {
        if (Document.FindFirstAnchor(anchor => anchor.Row == Document.Caret.Row && anchor.UserData is Editor.LineLinkAnchorData linkData && linkData.Type == type) is { } linkAnchor) {
            return CreateEntry("", ((Editor.LineLinkAnchorData)linkAnchor.UserData!).OnUse);
        }

        return null;
    }
}
