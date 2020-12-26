using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CelesteStudio.TtilebarButton.Themes;
using CelesteStudio.TtilebarButton.Utils;

namespace CelesteStudio.TtilebarButton
{
    /// <summary>
    ///     The menu for handling the render of buttons over the title bar.
    /// </summary>
    internal sealed class ActiveMenuForm : Form, IActiveMenu
    {
        /// <summary>
        ///     A internal hashtable of instances against form objects to
        ///     ensure only one instance is even created per form.
        /// </summary>
        private static readonly Dictionary<Form, IActiveMenu> s_parents;

        /// <summary>
        ///     The internal list of buttons to be rendered by the menu instance.
        /// </summary>
        private readonly ActiveButtonCollection _items;
        private int _layoutSuspendCount;

        /// <summary>
        ///     The instance's parent form to which it's attached.
        /// </summary>
        private readonly Form _parentForm;
        private readonly SpillOverMode _spillOverMode;
        private readonly Size _originalMinSize;
        private readonly int _leftAdjust;
        private readonly int _topAdjust;

        /// <summary>
        ///     Stores the max width of the menu control, as this resizes when buttons
        ///     are hidden and can throw out visibility calcs later if we don't store.
        /// </summary>
        private int _containerMaxWidth;

        private bool _isActivated;
        private ITheme _theme;
        private ToolTip _tooltip;

        /// <summary>
        ///     Gets the list of buttons for this menu instance.
        /// </summary>
        public IActiveButtonCollection Items => _items;

        /// <summary>
        ///     Gets or sets the tool tip control used for rendering tool tips.
        /// </summary>
        /// <value>The tool tip settings.</value>
        public ToolTip ToolTip
        {
            get => _tooltip ?? (_tooltip = new ToolTip {
                AutoPopDelay = 15000,
                ShowAlways = true,
                InitialDelay = 200,
                ReshowDelay = 200,
                UseAnimation = true
            });
            set => _tooltip = value;
        }

        /// <summary>
        ///     Sets up the window as a child window, so that it does not take focus
        ///     from the parent when clicked. This is attached to the desktop, as it doesn't
        ///     allow us to attach to the parent at this time. Subsequent
        ///     calls in the constructor will change the window's parent after the handle
        ///     has been created. This sequence is important to ensure the child is attached
        ///     and respects the z-ordering of the parent.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.Style = (int)Win32.WS_CHILD;
                p.Style |= (int)Win32.WS_CLIPSIBLINGS;
                p.ExStyle &= (int)Win32.WS_EX_LAYERED;
                p.ExStyle |= (int)Win32.WS_EX_COMPOSITED;
                p.Parent = Win32.GetDesktopWindow();
                return p;
            }
        }

        static ActiveMenuForm()
        {
            s_parents = new Dictionary<Form, IActiveMenu>();
        }
        /// <summary>
        ///     Constructor sets up the menu and sets the required properties
        ///     in order thay this may be displayed over the top of it's parent
        ///     form.
        /// </summary>
        private ActiveMenuForm(Form form, int leftAdjust, int topAdjust)
        {
            InitializeComponent();

            _items = new ActiveButtonCollection(this);

            _parentForm = form;
            _isActivated = form.WindowState != FormWindowState.Minimized;
            _leftAdjust = leftAdjust;
            _topAdjust = topAdjust;
            _originalMinSize = form.MinimumSize;
            _theme = ThemeFactory.GetTheme(form);
            _spillOverMode = SpillOverMode.Hide;

            SetStyle(ControlStyles.DoubleBuffer, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Opaque, false);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw, false);

            _parentForm.Disposed += ParentFormDisposed;
            Show(form);

            DoubleBuffered = true;
            Visible = false;
            AttachHandlers();

            ToolTip.ShowAlways = true;
            TopMost = form.TopMost;
            TopMost = false;
        }


        /// <summary>
        ///     The standard properties of the menu. Changing properties or when they are set
        ///     can effect the ability to attach to a parent, and leave the menu flaoting on the
        ///     desktop.
        /// </summary>
        private void InitializeComponent()
        {
            SuspendLayout();

            ClientSize = new Size(10, 10);
            ControlBox = false;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ActiveMenu";
            ShowIcon = false;
            ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Hide;
            Font = new Font("Segoe UI", 7.5F, FontStyle.Regular);

            ResumeLayout(false);
        }
        /// <summary>
        ///     Raises the <see cref="E:System.Windows.Forms.Form.Load"></see> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs"></see> that contains the event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BringToFront();
        }

        /// <summary>
        ///     Setup the handlers to reposition and resize the buttons when the parent
        ///     is resized or styles are changed.
        /// </summary>
        private void AttachHandlers()
        {
            _parentForm.Deactivate += ParentFormDeactivate;
            _parentForm.Activated += ParentFormActivated;
            _parentForm.SizeChanged += ParentSizeChanged;
            _parentForm.VisibleChanged += ParentRefresh;
            _parentForm.Move += ParentRefresh;
            _parentForm.SystemColorsChanged += ParentTitleButtonSystemColorsChanged;

            // used to mask the menu control behind the buttons.
            if (Win32.DwmIsCompositionEnabled)
            {
                BackColor = Color.Fuchsia;
                TransparencyKey = Color.Fuchsia;
            }
            else
            {
                BackColor = Color.FromKnownColor(KnownColor.ActiveCaption);
                TransparencyKey = BackColor;
            }
        }

        /// <summary>
        ///     Enabled the tooltip control when the parent form is active. This is necessary
        ///     because the menu form is never active.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
        private void ParentFormActivated(object sender, EventArgs e)
        {
            ToolTip.ShowAlways = true;
        }
        /// <summary>
        ///     Disables the tootips when the parent is activated.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
        private void ParentFormDeactivate(object sender, EventArgs e)
        {
            ToolTip.ShowAlways = false;
        }
        /// <summary>
        ///     Remove the parent from the hashtable when disposed.
        /// </summary>
        private void ParentFormDisposed(object sender, EventArgs e)
        {
            var form = (Form)sender;
            if (form == null)
                return;

            if (s_parents.ContainsKey(form))
                s_parents.Remove(form);
        }
        /// <summary>
        ///     When the style is changed we need to re-calc button sizes as well as positions.
        /// </summary>
        private void ParentTitleButtonSystemColorsChanged(object sender, EventArgs e)
        {
            _theme = ThemeFactory.GetTheme(_parentForm);
            CalcSize();
            OnPosition();
        }
        /// <summary>
        ///     Handle changes to the parent, and make sure the menu is aligned to match.
        /// </summary>
        private void ParentRefresh(object sender, EventArgs e)
        {
            if (_parentForm.WindowState == FormWindowState.Minimized || !_parentForm.Visible)
            {
                _isActivated = false;
                Visible = false;
            }
            else
            {
                _isActivated = true;
                OnPosition();
            }
        }
        private void ParentSizeChanged(object sender, EventArgs e)
        {
            if (_parentForm.WindowState == FormWindowState.Minimized)
            {
                _isActivated = false;
                Visible = false;
            }
            else
            {
                _isActivated = true;
                CalcSize();
                OnPosition();
            }
        }

        /// <summary>
        ///     Handles the CollectionModified event of the items control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs" /> instance containing the event data.</param>
        internal void OnItemsCollectionChanged()
        {
            SuspendLayout();
            try
            {
                ToolTip.RemoveAll();

                CollectionHelper.Synchronize(Items, Controls);

                foreach (var button in Items)
                    OnButtonToolTipChanged(button);

                CalcSize();
                OnPosition();
            }
            finally
            {
                ResumeLayout(true);
            }
        }
        internal void OnButtonToolTipChanged(IActiveButton button)
        {
            if (!string.IsNullOrEmpty(button.ToolTipTitle))
                ToolTip.ToolTipTitle = button.ToolTipTitle;

            if (!string.IsNullOrEmpty(button.ToolTipText))
                ToolTip.SetToolTip((Control)button, button.ToolTipText);
        }

        /// <summary>
        ///     Work out the buttons sizes based of sys diamensions. This doesn't work quite as expected
        ///     as the buttons seem to have larger borders, which change per theme.
        /// </summary>
        private void CalcSize()
        {
            var left = 0;
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                var button = (ActiveButton)Items[i];
                button.Theme = _theme;

                button.CalcButtonSize(_parentForm.WindowState == FormWindowState.Maximized);
                button.Location = new Point(left, _theme.ButtonOffset.Y + _topAdjust);
                button.Visible = true;

                left += button.Width + _theme.ButtonOffset.X + _leftAdjust * -1;
            }

            _containerMaxWidth = left;
            Size = new Size(_containerMaxWidth, Controls.Count == 0 ? 0 : Controls[0].Height);

            if (_spillOverMode == SpillOverMode.IncreaseSize)
            {
                var w = _containerMaxWidth + _theme.ControlBoxSize.Width + _theme.FrameBorder.Width + _theme.FrameBorder.Width;

                _parentForm.MinimumSize = _originalMinSize;

                if (_parentForm.MinimumSize.Width <= w)
                    _parentForm.MinimumSize = new Size(w, _parentForm.MinimumSize.Height);
            }
        }
        /// <summary>
        ///     Position the menu into the correct location, this varies per theme.
        /// </summary>
        private void OnPosition()
        {
            if (!IsDisposed)
            {
                if (_theme == null || !_theme.IsDisplayed)
                {
                    Visible = false;
                    return;
                }

                var top = _theme.FrameBorder.Height;
                var left = _theme.FrameBorder.Width + _theme.ControlBoxSize.Width;

                Location = new Point(
                    _parentForm.Left + _parentForm.Width - _containerMaxWidth - left,
                    top + _parentForm.Top);
                Visible = _theme.IsDisplayed && _isActivated;

                if (Visible)
                {
                    if (Items.Count > 0)
                    {
                        Opacity = _parentForm.Opacity;
                        if (_parentForm.Visible)
                            Opacity = _parentForm.Opacity;
                        else
                            Visible = false;
                    }

                    if (_spillOverMode == SpillOverMode.Hide)
                    {
                        var frameBorderWidth = _theme.FrameBorder.Width;

                        foreach (ActiveButton b in Items)
                            b.Visible = b.Left + Left - frameBorderWidth - 32 >= _parentForm.Left;
                    }
                }
            }
        }

        void IActiveMenu.SuspendLayout()
        {
            ++_layoutSuspendCount;
        }
        void IActiveMenu.ResumeLayout()
        {
            if (--_layoutSuspendCount == 0)
                OnItemsCollectionChanged();
        }

        /// <summary>
        ///     Creates or returns the menu instance for a given form.
        /// </summary>
        public static IActiveMenu GetInstance(Form form, int leftAdjust = 0, int topAdjust = 0)
        {
            if (!s_parents.ContainsKey(form))
                s_parents.Add(form, new ActiveMenuForm(form, leftAdjust, topAdjust));

            return s_parents[form];
        }
    }
}
