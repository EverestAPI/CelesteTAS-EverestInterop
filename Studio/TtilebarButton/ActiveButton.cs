using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using CelesteStudio.TtilebarButton.Themes;
using CelesteStudio.TtilebarButton.Utils;

namespace CelesteStudio.TtilebarButton
{
    internal class ActiveButton : Button, IActiveButton
    {
        private Size buttonSize;
        private int buttonX;
        private int buttonY;
        private Size textSize;
        private ITheme theme;
        private string _toolTipText;
        private string _toolTipTitle;
        private string _text;

        /// <summary>
        ///     Gets or sets the text property of the button control.
        /// </summary>
        /// <value>The text.</value>
        public new string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    CalcButtonSize();
                }
            }
        }

        public string ToolTipText
        {
            get => _toolTipText;
            set
            {
                if (_toolTipText != value)
                {
                    _toolTipText = value;
                    (Parent as ActiveMenuForm)?.OnButtonToolTipChanged(this);
                }
            }
        }

        public string ToolTipTitle
        {
            get => _toolTipTitle;
            set
            {
                if (_toolTipTitle != value)
                {
                    _toolTipTitle = value;
                    (Parent as ActiveMenuForm)?.OnButtonToolTipChanged(this);
                }
            }
        }

        public override Color BackColor
        {
            get => base.BackColor;
            set
            {
                if (value.IsEmpty)
                    value = Color.WhiteSmoke;

                base.BackColor = value;
            }
        }

        /// <summary>
        ///     Gets or sets the theme.
        /// </summary>
        /// <value>The theme.</value>
        internal ITheme Theme
        {
            get => theme;
            set
            {
                if (theme != value)
                {
                    theme = value;

                    var offset = value?.ImageOffset ?? Point.Empty;
                    Padding = new Padding(
                        offset.X > 0 ? offset.X : 0,
                        offset.Y > 0 ? offset.Y : 0,
                        offset.X < 0 ? -offset.X : 0,
                        offset.Y < 0 ? -offset.Y : 0);

                    CalcButtonSize();
                }
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActiveButton" /> class.
        /// </summary>
        public ActiveButton()
        {
            Initialize();
            ImageAlign = ContentAlignment.MiddleCenter;
        }


        /// <summary>
        ///     Initializes this instance.
        /// </summary>
        protected void Initialize()
        {
            if (Win32.DwmIsCompositionEnabled || Application.RenderWithVisualStyles)
                base.BackColor = Color.Transparent;
            else
                base.BackColor = Color.FromKnownColor(KnownColor.Control);

            SystemColorsChanged += ActiveButton_SystemColorsChanged;

            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TextAlign = ContentAlignment.MiddleCenter;
            Visible = false;

            CalcButtonSize();
        }

        /// <summary>
        ///     Calculates the size of the button.
        /// </summary>
        public void CalcButtonSize(bool maximized = false)
        {
            if (theme != null)
            {
                theme.Maximized = maximized;

                buttonSize = theme.SystemButtonSize;

                if (BackColor == Color.Empty)
                    BackColor = theme.BackColor;

                if (ForeColor == Color.Empty)
                    ForeColor = Color.Black;

                if (theme.ForceFlat)
                {
                    FlatStyle = FlatStyle.Flat;
                    FlatAppearance.BorderSize = 0;

                    buttonSize.Height -= 6;
                }
            }
            else
            {
                buttonSize = SystemInformation.CaptionButtonSize;
            }

            var width = buttonSize.Width;
            var height = buttonSize.Height;

            using (var e = Graphics.FromHwnd(Handle))
            {
                var text = Text;
                if (text?.Length > 23)
                    text = text.Substring(0, 20) + "...";

                textSize = e.MeasureString(text, Font, new SizeF(300, 40), StringFormat.GenericTypographic).ToSize();
            }

            if (width < textSize.Width + 20)
                width = textSize.Width + 20;

            buttonX = (width - textSize.Width) / 2 - 1;
            buttonY = (height - textSize.Height) / 2 - 1;

            Size = new Size(width, height);
        }

        /// <summary>
        ///     Handles the SystemColorsChanged event of the ActiveButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
        private void ActiveButton_SystemColorsChanged(object sender, EventArgs e)
        {
            CalcButtonSize();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (Image == null)
            {
                var color = Enabled ? ForeColor : ForeColor.Lerp(Color.White, 0.40f);

                using (var sf = StringFormat.GenericTypographic)
                using (var brush = new SolidBrush(color))
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Alignment = StringAlignment.Center;

                    var text = Text;
                    if (text?.Length > 23)
                        text = text.Substring(0, 20) + "...";

                    e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    e.Graphics.DrawString(text, Font, brush, ClientRectangle, sf);
                }
            }
        }
    }
}
