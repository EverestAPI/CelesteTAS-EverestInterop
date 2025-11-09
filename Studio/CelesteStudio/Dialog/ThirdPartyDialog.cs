using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication.Util;
using System.Collections.Generic;

namespace CelesteStudio.Tool;

public class ThirdPartyDialog : Form {
    private static readonly Dictionary<string, ThirdPartyDialog> Instances = new();

    private readonly TextViewer viewer;

    private ThirdPartyDialog(string title, string text) {
        Title = title;
        Icon = Assets.AppIcon;

        var viewerScrollable = new Scrollable {
            Width = Size.Width,
            Height = Size.Height,
        }.FixBorder();
        viewer = new TextViewer(Document.Create(text.SplitLines()), viewerScrollable);
        viewerScrollable.Content = viewer;

        Content = viewerScrollable;

        Size = new Size(400, 600);

        Studio.RegisterWindow(this);
    }

    public static void Show(string id, string title, string text) {
        if (Instances.TryGetValue(id, out var existing)) {
            existing.Title = title;

            using var __ = existing.viewer.Document.Update();
            using var patch = new Document.Patch(existing.viewer.Document);

            patch.DeleteRange(0, existing.viewer.Document.Lines.Count - 1);
            patch.InsertRange(0, text.SplitLines());
        } else {
            var dialog = new ThirdPartyDialog(title, text);
            dialog.Show();
            dialog.Closed += (_, _) => Instances.Remove(id);

            Instances[id] = dialog;
        }
    }
}
