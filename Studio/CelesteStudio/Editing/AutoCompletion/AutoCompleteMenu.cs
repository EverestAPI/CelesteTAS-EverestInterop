using CelesteStudio.Communication;
using CelesteStudio.Controls;
using Eto.Forms;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CelesteStudio.Editing.AutoCompletion;

public abstract class AutoCompleteMenu(TextEditor editor) : PopupMenu {

    protected readonly TextEditor editor = editor;
    protected Document Document => editor.Document;

    public abstract void Refresh(bool open = true);

    // While there are quick-edits available, Tab will cycle through them
    public override bool HandleKeyDown(KeyEventArgs e) => HandleKeyDown(e, useTabComplete: !editor.GetQuickEdits().Any());
}
