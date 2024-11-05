using CelesteStudio.Controls;
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

            var bounds = new CGRect(drawable.DrawX, drawable.DrawY, drawable.DrawWidth * Window.BackingScaleFactor, drawable.DrawHeight * Window.BackingScaleFactor);
            var info = new SKImageInfo((int)bounds.Width, (int)bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

            // Allocate a memory for the drawing process
            nuint infoLength = (nuint)info.BytesSize;
            if (bitmapData?.Length != infoLength) {
                dataProvider?.Dispose();
                bitmapData?.Dispose();

                bitmapData = NSMutableData.FromLength(infoLength);
                dataProvider = new CGDataProvider(bitmapData.MutableBytes, info.BytesSize);

                surface?.Dispose();
                surface = SKSurface.Create(info, bitmapData.MutableBytes, info.RowBytes);
            }

            using (new SKAutoCanvasRestore(surface!.Canvas, true)) {
                drawable.Draw(surface);
            }
            surface.Canvas.Flush();

            using var image = new CGImage(
                info.Width, info.Height,
                8, info.BitsPerPixel, info.RowBytes,
                colorSpace, CGBitmapFlags.ByteOrder32Big | CGBitmapFlags.PremultipliedLast,
                dataProvider, null, false, CGColorRenderingIntent.Default);

            var ctx = NSGraphicsContext.CurrentContext.GraphicsPort;
            ctx.DrawImage(bounds, image);
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
