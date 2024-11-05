using CelesteStudio.Controls;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eto.Wpf.Forms;
using SkiaSharp;
using System.Windows.Controls;
using Eto.Wpf;

namespace CelesteStudio.WPF;

public class SkiaDrawableHandler : WpfPanel<Border, SkiaDrawable, SkiaDrawable.ICallback>, SkiaDrawable.IHandler {
    public void Create() { 
        Control = new SkiaBorder(Widget);
    }

    private class SkiaBorder(SkiaDrawable drawable) : Border {
        private SKSurface? surface;
        private WriteableBitmap? bitmap;

        protected override void OnRender(DrawingContext drawingContext) {
            base.OnRender(drawingContext);

            var m = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            double dpiX = m.M11;
            double dpiY = m.M22;
            int width = (int)(drawable.DrawWidth * dpiX);
            int height = (int)(drawable.DrawHeight * dpiY);

            if (width == 0 || height == 0) {
                return;
            }

            if (width != bitmap?.PixelWidth || height != bitmap?.PixelHeight) {
                bitmap = new WriteableBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32, null);

                surface?.Dispose();
                surface = SKSurface.Create(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul), bitmap.BackBuffer, bitmap.BackBufferStride);
            }

            bitmap.Lock();

            using (new SKAutoCanvasRestore(surface!.Canvas, true)) {
                drawable.Draw(surface);
            }
            surface.Canvas.Flush();

            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            drawingContext.DrawImage(bitmap, new Rect(drawable.DrawX, drawable.DrawY, width, height));
            bitmap.Unlock();
        }
    }

    public bool CanFocus {
        get => Control.Focusable;
        set {
            if (value != Control.Focusable) {
                Control.Focusable = value;
            }
        }
    }

    public override Eto.Drawing.Color BackgroundColor {
        get => Control.Background.ToEtoColor();
        set => Control.Background = value.ToWpfBrush(Control.Background);
    }

    public override void SetContainerContent(FrameworkElement content) {
        Control.Child = content;
    }

}
