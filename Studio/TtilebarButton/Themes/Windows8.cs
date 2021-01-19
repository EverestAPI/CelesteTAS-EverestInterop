using System.Drawing;
using System.Windows.Forms;

namespace CelesteStudio.TtilebarButton.Themes
{
    internal class Windows8 : Aero
    {
        public override bool ForceFlat => true;

        public override Point ButtonOffset
        {
            get
            {
                if (buttonOffset == Point.Empty)
                {
                    if (IsToolbar)
                        buttonOffset = new Point(-40, 0);
                    else
                        buttonOffset = new Point(-40, 0);
                }

                return buttonOffset;
            }
        }
        public override Size SystemButtonSize
        {
            get
            {
                if (IsToolbar)
                {
                    var size = SystemInformation.SmallCaptionButtonSize;
                    size.Height += 4;
                    size.Width = 36;
                    systemButtonSize = size;
                }
                else
                {
                    var size = SystemInformation.CaptionButtonSize;
                    size.Height += base.Maximized ? 0 : 6;
                    size.Width = 48;
                    systemButtonSize = size;
                }

                return systemButtonSize;
            }
        }

        public Windows8(Form form) : base(form) { }
    }
}
