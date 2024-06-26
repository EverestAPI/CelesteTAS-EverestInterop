using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio;

public class Editor : RichTextArea {
    
    public Editor() {
        Font = new Font(new FontFamily("JetBrains Mono"), 9.75f);
    }
}