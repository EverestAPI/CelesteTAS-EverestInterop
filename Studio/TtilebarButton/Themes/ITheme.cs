using System.Drawing;

namespace CelesteStudio.TtilebarButton.Themes
{
    internal interface ITheme
    {
        Color BackColor { get; }

        Point ButtonOffset { get; }

        Point ImageOffset { get; }

        Size ControlBoxSize { get; }

        bool ForceFlat { get; }

        Size FrameBorder { get; }

        bool IsDisplayed { get; }

        bool Maximized { get; set; }

        Size SystemButtonSize { get; }

    }
}
