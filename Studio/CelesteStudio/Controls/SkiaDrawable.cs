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

    // private readonly SKColorType colorType = Platform.Instance.IsWinForms || Platform.Instance.IsWpf ? SKColorType.Bgra8888 : SKColorType.Rgba8888;

    // private Bitmap? image = null;
    // private SKImageInfo imageInfo = SKImageInfo.Empty;

    public virtual int DrawX => 0;
    public virtual int DrawY => 0;
    public virtual int DrawWidth => Width;
    public virtual int DrawHeight => Height;

    public bool CanFocus {
        get => Handler.CanFocus;
        set => Handler.CanFocus = value;
    }


    public virtual void Draw(SKSurface surface) { }

    // protected virtual void OnPaint(PaintEventArgs e)
    // {
    //     try {
    //         int drawWidth = DrawWidth, drawHeight = DrawHeight;
    //
    //         if (drawWidth <= 0 || drawHeight <= 0) {
    //             return;
    //         }
    //
    //         if (drawWidth != image?.Size.Width || drawHeight != image?.Size.Height)
    //         {
    //             image?.Dispose();
    //             image = new Bitmap(new Size(drawWidth, drawHeight), PixelFormat.Format32bppRgba);
    //             imageInfo = new SKImageInfo(drawWidth, drawHeight, colorType, SKAlphaType.Unpremul);
    //         }
    //
    //         using var bmp = image.Lock();
    //         using var surface = SKSurface.Create(imageInfo, bmp.Data, bmp.ScanWidth);
    //
    //         Draw(e, surface, imageInfo);
    //
    //         e.Graphics.DrawImage(image, DrawX, DrawY);
    //     }
    //     catch (Exception ex)
    //     {
    //         Console.WriteLine(ex);
    //         e.Graphics.DrawText(Fonts.Monospace(12.0f), Colors.Red, PointF.Empty, ex.ToString());
    //     }
    // }

    protected new class Callback : Control.Callback, ICallback {
        public void Draw(SkiaDrawable widget, SKSurface surface) {
            using (widget.Platform.Context)
                widget.Draw(surface);
        }
    }

    [AutoInitialize(false)]
    public new interface IHandler : Panel.IHandler {
        void Create();
        bool CanFocus { get; set; }
    }
    public new interface ICallback : Control.ICallback {
        void Draw(SkiaDrawable widget, SKSurface surface);
    }
}
