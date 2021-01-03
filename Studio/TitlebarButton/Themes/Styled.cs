using System.Drawing;
using System.Windows.Forms;

namespace CelesteStudio.TtilebarButton.Themes
{
    internal class Styled : ThemeBase
    {
        public override Color BackColor => Color.Transparent;

        public override Size FrameBorder
        {
            get
            {
                if (frameBorder == Size.Empty)
                    switch (form.FormBorderStyle)
                    {
                        case FormBorderStyle.SizableToolWindow:
                        case FormBorderStyle.Sizable:
                            frameBorder = new Size(SystemInformation.FrameBorderSize.Width + 1,
                                SystemInformation.FrameBorderSize.Height + 1);
                            break;
                        default:
                            frameBorder = new Size(SystemInformation.Border3DSize.Width + 2,
                                SystemInformation.Border3DSize.Height + 2);
                            break;
                    }
                return frameBorder;
            }
        }

        public override Size SystemButtonSize
        {
            get
            {
                if (systemButtonSize == Size.Empty)
                {
                    if (IsToolbar)
                    {
                        var size = base.SystemButtonSize;
                        size.Height += 2;
                        size.Width -= 1;
                        systemButtonSize = size;
                    }
                    else
                    {
                        var size = SystemInformation.CaptionButtonSize;
                        size.Height -= 2;
                        size.Width -= 2;
                        systemButtonSize = size;
                    }
                }

                return systemButtonSize;
            }
        }
        public Styled(Form form)
            : base(form)
        {
        }
    }
}
