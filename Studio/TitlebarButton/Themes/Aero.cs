using System.Drawing;
using System.Windows.Forms;

// ReSharper disable IdentifierTypo

namespace CelesteStudio.TtilebarButton.Themes
{
    internal class Aero : ThemeBase
    {
        private Size maxFrameBorder = Size.Empty;
        private Size minFrameBorder = Size.Empty;

        public override Color BackColor => Color.Transparent;

        public override Point ButtonOffset
        {
            get
            {
                if (buttonOffset == Point.Empty)
                {
                    if (IsToolbar)
                        buttonOffset = new Point(0, 0);
                    else
                        buttonOffset = new Point(0, -2);
                }

                return buttonOffset;
            }
        }
        public override Size ControlBoxSize
        {
            get
            {
                if (controlBoxSize == Size.Empty)
                {
                    if (IsToolbar)
                    {
                        if (form.ControlBox)
                            controlBoxSize = new Size(SystemButtonSize.Width, SystemButtonSize.Height);
                        else
                            controlBoxSize = new Size(1, 0);
                    }
                    else
                    {
                        if (!form.MaximizeBox && !form.MinimizeBox && form.ControlBox)
                        {
                            if (form.HelpButton)
                                controlBoxSize = new Size(2 * SystemButtonSize.Width + 7, SystemButtonSize.Height);
                            else
                                controlBoxSize = new Size(1 * SystemButtonSize.Width + 13, SystemButtonSize.Height);
                        }
                        else
                        {
                            int index;
                            index = form.ControlBox ? 3 : 0;
                            controlBoxSize = new Size(index * SystemButtonSize.Width, SystemButtonSize.Height);
                        }
                    }
                }

                return controlBoxSize;
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
                        var size = SystemInformation.SmallCaptionButtonSize;
                        size.Height += 2;
                        size.Width += 2;
                        systemButtonSize = size;
                    }
                    else
                    {
                        var size = SystemInformation.CaptionButtonSize;
                        size.Height += 1;
                        //size.Width -= 1;
                        systemButtonSize = size;
                    }
                }

                return systemButtonSize;
            }
        }
        public override Size FrameBorder
        {
            get
            {
                if (form.WindowState == FormWindowState.Maximized)
                {
                    if (maxFrameBorder == Size.Empty)
                        switch (form.FormBorderStyle)
                        {
                            case FormBorderStyle.FixedToolWindow:
                                maxFrameBorder = new Size(SystemInformation.FrameBorderSize.Width - 8, -1);
                                break;
                            case FormBorderStyle.SizableToolWindow:
                                maxFrameBorder = new Size(SystemInformation.FrameBorderSize.Width - 3, 4);
                                break;
                            case FormBorderStyle.Sizable:
                                maxFrameBorder = new Size(SystemInformation.FrameBorderSize.Width + 2, 7);
                                break;
                            default:
                                maxFrameBorder = new Size(SystemInformation.FrameBorderSize.Width - 3, 2);
                                break;
                        }
                    return maxFrameBorder;
                }

                if (minFrameBorder == Size.Empty)
                    switch (form.FormBorderStyle)
                    {
                        case FormBorderStyle.FixedToolWindow:
                            minFrameBorder = new Size(SystemInformation.FrameBorderSize.Width - 8, -1);
                            break;
                        case FormBorderStyle.SizableToolWindow:
                            minFrameBorder = new Size(SystemInformation.FrameBorderSize.Width - 3, 4);
                            break;
                        case FormBorderStyle.Sizable:
                            minFrameBorder = new Size(SystemInformation.FrameBorderSize.Width, 1);
                            break;
                        case FormBorderStyle.Fixed3D:
                            minFrameBorder = new Size(SystemInformation.Border3DSize.Width, -4);
                            break;
                        case FormBorderStyle.FixedSingle:
                            minFrameBorder = new Size(SystemInformation.Border3DSize.Width - 2, -4);
                            break;
                        default:
                            minFrameBorder = new Size(SystemInformation.Border3DSize.Width - 1, -4);
                            break;
                    }
                return minFrameBorder;
            }
        }

        public Aero(Form form) : base(form) { }
    }
}
