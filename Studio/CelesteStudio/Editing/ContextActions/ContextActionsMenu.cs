using Eto.Forms;
using StudioCommunication;
using System.Collections.Generic;
using System.Linq;

namespace CelesteStudio.Editing.ContextActions;

public class ContextActionsMenu : PopupMenu {

    // These should be ordered from most specific to most applicable.
    public static readonly ContextAction[] ContextActions = [
        new CombineConsecutiveSameInputs(),

        new SwapActions(Actions.Left, Actions.Right),
        new SwapActions(Actions.Jump, Actions.Jump2),
        new SwapActions(Actions.Dash, Actions.Dash2),

        new ForceCombineInputFrames(),
        new SplitFrames(),

        new CreateRepeatCommand(),
        new InlineRepeatCommand(),
        new InlineReadCommand(),

        new UseLineLink(Editor.LineLinkType.OpenReadFile),
        new UseLineLink(Editor.LineLinkType.GoToPlayLine),
    ];

    private readonly Editor editor;
    private Document Document => editor.Document;

    public ContextActionsMenu(Editor editor) {
        this.editor = editor;
    }

    public void Refresh() {
        Entries = ContextActions
            .Select(contextAction => {
                var hotkey = Settings.Instance.KeyBindings.GetValueOrDefault(contextAction.Identifier, contextAction.DefaultHotkey);

                return contextAction.Check() ?? new PopupMenu.Entry {
                    DisplayText = contextAction.DisplayName,
                    SearchText = contextAction.DisplayName,
                    ExtraText = hotkey.KeysOrNone != Keys.None ? hotkey.ToShortcutString() : string.Empty,
                    Disabled = true,
                    OnUse = () => {},
                };
            })
            .OrderBy(entry => entry.Disabled ? 1 : 0)
            .ToList();

        if (Entries.Count > 0) {
            editor.OpenPopupMenu(this);
        }
    }

    // Using tab doesn't feel "right" for the context actions menu
    public override bool HandleKeyDown(KeyEventArgs e) => HandleKeyDown(e, useTabComplete: false);
}
