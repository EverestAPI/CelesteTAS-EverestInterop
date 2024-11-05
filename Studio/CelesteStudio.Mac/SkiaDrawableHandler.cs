using CelesteStudio.Controls;
using Eto.Mac.Forms;
using MonoMac.AppKit;
using MonoMac.CoreGraphics;
using SkiaSharp;
using System;
using System.Runtime.InteropServices;

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
        private IntPtr bitmapData;
        private int lastLength;

        private SKSurface? surface;

        private readonly CGColorSpace colorSpace = CGColorSpace.CreateDeviceRGB();
        private CGDataProvider? dataProvider;
        private CGImage? image;

        public override void DrawRect(CGRect dirtyRect)
        {
            base.DrawRect(dirtyRect);

            var bounds = new CGRect(drawable.DrawX, drawable.DrawY, drawable.DrawWidth, drawable.DrawHeight);
            var info = new SKImageInfo((int)bounds.Width, (int)bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

            // Allocate a memory block for the drawing process
            int newLength = info.BytesSize;
            if (lastLength != newLength)
            {
                lastLength = newLength;
                if (bitmapData != IntPtr.Zero) {
                    bitmapData = Marshal.ReAllocCoTaskMem(bitmapData, newLength);
                } else {
                    bitmapData = Marshal.AllocCoTaskMem(newLength);
                }

                surface?.Dispose();
                surface = SKSurface.Create(info, bitmapData, info.RowBytes);

                dataProvider?.Dispose();
                dataProvider = new CGDataProvider(bitmapData, lastLength);

                image?.Dispose();
                image = new CGImage(
                    info.Width, info.Height,
                    8, info.BitsPerPixel, info.RowBytes,
                    colorSpace, CGBitmapFlags.ByteOrder32Big | CGBitmapFlags.PremultipliedLast,
                    dataProvider, null, false, CGColorRenderingIntent.Default);
            }

            using (new SKAutoCanvasRestore(surface!.Canvas, true)) {
                drawable.Draw(surface);
            }
            surface.Canvas.Flush();

            var ctx = NSGraphicsContext.CurrentContext.GraphicsPort;
            ctx.DrawImage(bounds, image);
        }

        protected override void Dispose(bool disposing) {
            Marshal.FreeCoTaskMem(bitmapData);

            bitmapData = IntPtr.Zero;
            lastLength = 0;

            surface?.Dispose();
            surface = null;

            colorSpace.Dispose();

            dataProvider?.Dispose();
            dataProvider = null;

            image?.Dispose();
            image = null;
        }
    }
}
