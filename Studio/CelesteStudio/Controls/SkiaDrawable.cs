using Eto;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using System;

namespace CelesteStudio.Controls;

[Handler(typeof(IHandler))]
public class SkiaDrawable : Panel {
    private static readonly object callback = new Callback();

    protected override object GetCallback() => callback;
    protected new IHandler Handler => (IHandler)base.Handler;

    public SkiaDrawable() {
        Handler.Create();
        Initialize();
    }

    // Limits the bounds of the drawn region, for optimization
    public virtual int DrawX => 0;
    public virtual int DrawY => 0;
    public virtual int DrawWidth => Width;
    public virtual int DrawHeight => Height;

    /// Whether the control can currently be drawn
    public virtual bool CanDraw => true;

    public bool CanFocus {
        get => Handler.CanFocus;
        set => Handler.CanFocus = value;
    }

    public virtual void Draw(SKSurface surface) { }

    [AutoInitialize(false)]
    public new interface IHandler : Panel.IHandler {
        void Create();
        bool CanFocus { get; set; }
    }
}
