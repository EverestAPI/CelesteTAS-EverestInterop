using System;

namespace CelesteStudio.RichText {
public class AutocompleteItem {
    public int ImageIndex = -1;
    string menuText;
    public object Tag;
    public string Text;
    string toolTipText;
    string toolTipTitle;


    public AutocompleteItem() { }

    public AutocompleteItem(string text) {
        Text = text;
    }

    public AutocompleteItem(string text, int imageIndex)
        : this(text) {
        this.ImageIndex = imageIndex;
    }

    public AutocompleteItem(string text, int imageIndex, string menuText)
        : this(text, imageIndex) {
        this.menuText = menuText;
    }

    public AutocompleteItem(string text, int imageIndex, string menuText, string toolTipTitle, string toolTipText)
        : this(text, imageIndex, menuText) {
        this.toolTipTitle = toolTipTitle;
        this.toolTipText = toolTipText;
    }

    public AutocompleteMenu Parent { get; internal set; }

    public virtual string ToolTipTitle {
        get { return toolTipTitle; }
        set { toolTipTitle = value; }
    }

    public virtual string ToolTipText {
        get { return toolTipText; }
        set { toolTipText = value; }
    }

    public virtual string MenuText {
        get { return menuText; }
        set { menuText = value; }
    }

    public virtual string GetTextForReplace() {
        return Text;
    }

    public virtual CompareResult Compare(string fragmentText) {
        if (Text.StartsWith(fragmentText, StringComparison.InvariantCultureIgnoreCase) &&
            Text != fragmentText) {
            return CompareResult.VisibleAndSelected;
        }

        return CompareResult.Hidden;
    }

    public override string ToString() {
        return menuText ?? Text;
    }

    public virtual void OnSelected(AutocompleteMenu popupMenu, SelectedEventArgs e) {
        ;
    }
}

public enum CompareResult {
    /// <summary>
    /// Item do not appears
    /// </summary>
    Hidden,

    /// <summary>
    /// Item appears
    /// </summary>
    Visible,

    /// <summary>
    /// Item appears and will selected
    /// </summary>
    VisibleAndSelected,

    /// <summary>
    /// Item is exact match and want to auto correct
    /// </summary>
    ExactAndReplace
}

/// <summary>
/// Autocomplete item for code snippets
/// </summary>
/// <remarks>Snippet can contain special char ^ for caret position.</remarks>
public class SnippetAutocompleteItem : AutocompleteItem {
    public SnippetAutocompleteItem(string snippet) {
        Text = snippet.Replace("\r", "").Replace("\t", "    ");
        ToolTipTitle = "Code snippet:";
        ToolTipText = Text;
    }

    public override string ToString() {
        return MenuText ?? Text.Replace("\n", " ").Replace("^", "");
    }

    public override string GetTextForReplace() {
        return Text;
    }

    public override void OnSelected(AutocompleteMenu popupMenu, SelectedEventArgs e) {
        e.Tb.BeginUpdate();
        e.Tb.Selection.BeginUpdate();
        //remember places
        var p1 = popupMenu.Fragment.Start;
        var p2 = e.Tb.Selection.Start;
        //do auto indent
        if (e.Tb.AutoIndent) {
            for (int iLine = p1.iLine + 1; iLine <= p2.iLine; iLine++) {
                e.Tb.Selection.Start = new Place(0, iLine);
                e.Tb.DoAutoIndent(iLine);
            }
        }

        e.Tb.Selection.Start = p1;
        //move caret position right and find char ^
        while (e.Tb.Selection.CharBeforeStart != '^') {
            if (!e.Tb.Selection.GoRightThroughFolded()) {
                break;
            }
        }

        //remove char ^
        e.Tb.Selection.GoLeft(true);
        e.Tb.InsertText("");
        //
        e.Tb.Selection.EndUpdate();
        e.Tb.EndUpdate();
    }

    /// <summary>
    /// Compares fragment text with this item
    /// </summary>
    public override CompareResult Compare(string fragmentText) {
        if (Text.StartsWith(fragmentText, StringComparison.InvariantCultureIgnoreCase) && Text != fragmentText) {
            return CompareResult.Visible;
        }

        return CompareResult.Hidden;
    }
}

public class SnippetPlusAutocompleteItem : SnippetAutocompleteItem {
    private readonly string description;

    public SnippetPlusAutocompleteItem(string description, string snippet)
        : base(snippet) {
        this.description = description;
    }

    public override string ToString() {
        return MenuText ?? description;
    }

    public override void OnSelected(AutocompleteMenu popupMenu, SelectedEventArgs e) {
        e.Tb.BeginUpdate();
        e.Tb.Selection.BeginUpdate();
        //remember places
        var p1 = popupMenu.Fragment.Start;
        var p2 = e.Tb.Selection.Start;
        e.Tb.Selection.Start = p1;
        //move caret position right and find char ^
        while (e.Tb.Selection.CharBeforeStart != '^') {
            if (!e.Tb.Selection.GoRightThroughFolded()) {
                break;
            }
        }

        //remove char ^
        e.Tb.Selection.GoLeft(true);
        e.Tb.InsertText("");
        //
        e.Tb.Selection.EndUpdate();
        e.Tb.EndUpdate();
    }

    public override CompareResult Compare(string fragmentText) {
        if (description.StartsWith(fragmentText, StringComparison.OrdinalIgnoreCase) && description != fragmentText) {
            return CompareResult.Visible;
        }

        return CompareResult.Hidden;
    }
}

/// <summary>
/// This autocomplete item appears after dot
/// </summary>
public class MethodAutocompleteItem : AutocompleteItem {
    readonly string lowercaseText;
    string firstPart;

    public MethodAutocompleteItem(string text)
        : base(text) {
        lowercaseText = Text.ToLower();
    }

    public override CompareResult Compare(string fragmentText) {
        int i = fragmentText.LastIndexOf('.');
        if (i < 0) {
            return CompareResult.Hidden;
        }

        string lastPart = fragmentText.Substring(i + 1);
        firstPart = fragmentText.Substring(0, i);

        if (lastPart == "") {
            return CompareResult.Visible;
        }

        if (Text.StartsWith(lastPart, StringComparison.InvariantCultureIgnoreCase)) {
            return CompareResult.VisibleAndSelected;
        }

        if (lowercaseText.Contains(lastPart.ToLower())) {
            return CompareResult.Visible;
        }

        return CompareResult.Hidden;
    }

    public override string GetTextForReplace() {
        return firstPart + "." + Text;
    }
}
}