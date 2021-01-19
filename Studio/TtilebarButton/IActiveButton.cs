using System;
using System.Drawing;

namespace CelesteStudio.TtilebarButton
{
    /// <summary>
    ///     Defines an ActiveMenu item.
    /// </summary>
    public interface IActiveButton
    {
        /// <summary>
        ///     Click event.
        /// </summary>
        event EventHandler Click;

        /// <summary>
        ///     Gets or sets a value indicating whether the control can respond to user interaction.
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        ///     Gets or sets the object that contains data about the button.
        /// </summary>
        object Tag { get; set; }

        /// <summary>
        ///     Gets or sets the name of the button.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        ///     Gets or sets the image of the button.
        /// </summary>
        Image Image { get; set; }

        /// <summary>
        ///     Gets or sets button text.
        /// </summary>
        string Text { get; set; }

        /// <summary>
        ///     Gets or sets text color.
        /// </summary>
        Color ForeColor { get; set; }

        /// <summary>
        ///     Gets or sets background color.
        /// </summary>
        Color BackColor { get; set; }

        /// <summary>
        ///     Button tool tip.
        /// </summary>
        string ToolTipText { get; set; }

        /// <summary>
        ///     Button tool tip title.
        /// </summary>
        string ToolTipTitle { get; set; }
    }
}
