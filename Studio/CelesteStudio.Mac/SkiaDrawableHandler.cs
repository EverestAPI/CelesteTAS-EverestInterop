using CelesteStudio.Controls;
using CelesteStudio.Util;
using Eto.Mac.Forms;
using MonoMac.AppKit;
using MonoMac.CoreGraphics;
using MonoMac.Foundation;
using SkiaSharp;

namespace CelesteStudio.Mac;

public class SkiaDrawableHandler : MacPanel<SkiaDrawableHandler.SkiaDrawableView, SkiaDrawable, SkiaDrawable.ICallback>, SkiaDrawable.IHandler {
    public void Create() {
        Enabled = true;
        Control = new SkiaDrawableView(Widget);
    }

    public override NSView ContainerControl => Control;
    public bool CanFocus {
        get => true;
        set { }
    }

    public class SkiaDrawableView(SkiaDrawable drawable) : MacPanelView {
        private NSMutableData? bitmapData;
        private CGDataProvider? dataProvider;
        private SKSurface? surface;

        private readonly CGColorSpace colorSpace = CGColorSpace.CreateDeviceRGB();

        public override void DrawRect(CGRect dirtyRect) {
            base.DrawRect(dirtyRect);

            double scale = Window.BackingScaleFactor;
            var bounds = new CGRect(drawable.DrawX * scale, drawable.DrawY * scale, drawable.DrawWidth * scale, drawable.DrawHeight * scale);
            var info = new SKImageInfo((int)bounds.Width, (int)bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

            // Allocate a memory for the drawing process
            nuint infoLength = (nuint)info.BytesSize;
            if (surface == null || bitmapData?.Length != infoLength) {
                dataProvider?.Dispose();
                bitmapData?.Dispose();

                bitmapData = NSMutableData.FromLength(infoLength);
                dataProvider = new CGDataProvider(bitmapData.MutableBytes, info.BytesSize);

                surface?.Dispose();
                surface = SKSurface.Create(info, bitmapData.MutableBytes, info.RowBytes);
                surface.Canvas.Scale((float)scale);
                surface.Canvas.Save();
            }

            var canvas = surface.Canvas;
            using (new SKAutoCanvasRestore(canvas, true)) {
                canvas.Clear(drawable.BackgroundColor.ToSkia());
                canvas.Translate(-drawable.DrawX, -drawable.DrawY);
                drawable.Draw(surface);
            }
            canvas.Flush();

            using var image = new CGImage(
                info.Width, info.Height,
                8, info.BitsPerPixel, info.RowBytes,
                colorSpace, CGBitmapFlags.ByteOrder32Big | CGBitmapFlags.PremultipliedLast,
                dataProvider, null, false, CGColorRenderingIntent.Default);

            var ctx = NSGraphicsContext.CurrentContext.GraphicsPort;
            // NOTE: macOS uses a different coordinate-system
            ctx.DrawImage(new CGRect(bounds.X / scale, Bounds.Height - (bounds.Height + bounds.Y) / scale, bounds.Width / scale, bounds.Height / scale), image);
        }

        protected override void Dispose(bool disposing) {
            dataProvider?.Dispose();
            dataProvider = null;

            bitmapData?.Dispose();
            bitmapData = null;

            surface?.Dispose();
            surface = null;

            colorSpace.Dispose();
        }
    }
}
