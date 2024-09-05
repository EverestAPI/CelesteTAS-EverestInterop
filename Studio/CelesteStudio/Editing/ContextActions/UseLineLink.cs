using CelesteStudio.Data;

namespace CelesteStudio.Editing.ContextActions;

public class UseLineLink(MenuEntry entry) : ContextAction {
    public override MenuEntry Entry => entry;

    public override PopupMenu.Entry? Check() {
        if (Document.FindFirstAnchor(anchor => anchor.Row == Document.Caret.Row && anchor.UserData is Editor.LineLinkAnchorData linkData && linkData.Entry == entry) is { } linkAnchor) {
            return CreateEntry("", () => ((Editor.LineLinkAnchorData)linkAnchor.UserData!).OnUse());
        }

        return null;
    }
}
