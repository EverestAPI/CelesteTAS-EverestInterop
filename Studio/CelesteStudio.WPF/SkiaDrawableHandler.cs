using CelesteStudio.Controls;
using CelesteStudio.Util;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eto.Wpf.Forms;
using SkiaSharp;
using System.Windows.Controls;
using Eto.Wpf;

namespace CelesteStudio.WPF;

public class SkiaDrawableHandler : WpfPanel<Border, SkiaDrawable, Eto.Forms.Control.ICallback>, SkiaDrawable.IHandler {
    public void Create() {
        Control = new SkiaBorder(Widget);
    }

    private class SkiaBorder(SkiaDrawable drawable) : Border, IDisposable {
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

            if (drawable.CanDraw) {
                if (bitmap == null || surface == null || width != bitmap.PixelWidth || height != bitmap.PixelHeight) {
                    const double bitmapDpi = 96.0;
                    bitmap = new WriteableBitmap(width, height, bitmapDpi * dpiX, bitmapDpi * dpiY, PixelFormats.Pbgra32, null);

                    surface?.Dispose();
                    surface = SKSurface.Create(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul), bitmap.BackBuffer, bitmap.BackBufferStride, new SKSurfaceProperties(SKPixelGeometry.Unknown));
                    surface.Canvas.Scale((float)dpiX, (float)dpiY);
                    surface.Canvas.Save();
                }

                bitmap.Lock();

                var canvas = surface.Canvas;
                using (new SKAutoCanvasRestore(surface.Canvas, true)) {
                    canvas.Clear(drawable.BackgroundColor.ToSkia());
                    canvas.Translate(-drawable.DrawX, -drawable.DrawY);
                    drawable.Draw(surface);
                }
                canvas.Flush();
            } else {
                drawable.Invalidate();
            }

            if (bitmap != null) {
                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                drawingContext.DrawImage(bitmap, new Rect(drawable.DrawX, drawable.DrawY, width / dpiX, height / dpiY));
                bitmap.Unlock();
            }
        }

        ~SkiaBorder() {
            Dispose();
        }
        public void Dispose() {
            surface?.Dispose();
            surface = null;

            GC.SuppressFinalize(this);
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
