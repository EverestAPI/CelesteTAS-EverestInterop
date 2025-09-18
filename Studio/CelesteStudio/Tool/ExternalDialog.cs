using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Tool;

public class ExternalDialog : Eto.Forms.Dialog {
    private static ExternalDialog? Instance;

    private readonly TextBox textBox;

    private ExternalDialog(string title, string text) {
        Title = title;
        Icon = Assets.AppIcon;
        const int rowWidth = 400;
        textBox = new TextBox { Width = rowWidth, Text = text , ReadOnly = false };
        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Items = { textBox }
        };
        Resizable = false;
        
        Studio.RegisterDialog(this);
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }

    public static void Show(string title, string text) {
        Application.Instance.Invoke(() => {
            Instance?.Close();
            Instance = new ExternalDialog(title, text);
            Instance.ShowModalAsync();
        });
    }
}
