using System.Drawing;
using System.Windows.Forms;

namespace CelesteStudio.TtilebarButton.Themes
{
    internal class XPStyle : Styled
    {
        public override Color BackColor
        {
            get
            {
                if (backColor == Color.Empty)
                    backColor = Color.FromKnownColor(KnownColor.ActiveBorder);
                return backColor;
            }
        }

        public override Size FrameBorder
        {
            get
            {
                if (frameBorder == Size.Empty)
                    frameBorder = new Size(base.FrameBorder.Width + 2, base.FrameBorder.Height);
                return frameBorder;
            }
        }

        public XPStyle(Form form) : base(form) { }
    }
}
