using CelesteStudio.Controls;
using CelesteStudio.Util;
using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Eto.Wpf.Forms;
using SkiaSharp;
using System.Windows.Controls;
using Eto.Wpf;
using Eto;
using Eto.Drawing;
using Eto.Forms;
using System.Windows.Media;
using EtoControl = Eto.Forms.Control;
using EtoColor = Eto.Drawing.Color;
using EtoColors = Eto.Drawing.Colors;

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
            int imageWidth = (int)(drawable.ImageWidth * dpiX);
            int imageHeight = (int)(drawable.ImageHeight * dpiY);
            bool preRender = drawable.PreRenderImage;

            if (drawable.CanDraw) {
                if (bitmap == null || surface == null || imageWidth != bitmap.PixelWidth || imageHeight != bitmap.PixelHeight || Settings.Instance.WPFSkiaHack) {
                    if (imageWidth == 0 || imageHeight == 0) {
                        // A zero sized surface causes issues, so use a null 1x1
                        // drawable.Draw() still needs to be called, so simply skipping render is not an option
                        bitmap = null;

                        surface?.Dispose();
                        surface = SKSurface.CreateNull(1, 1);
                    } else {
                        const double bitmapDpi = 96.0;
                        bitmap = new WriteableBitmap(imageWidth, imageHeight, bitmapDpi * dpiX, bitmapDpi * dpiY, PixelFormats.Pbgra32, null);

                        surface?.Dispose();
                        surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight, SKImageInfo.PlatformColorType, SKAlphaType.Premul), bitmap.BackBuffer, bitmap.BackBufferStride, new SKSurfaceProperties(SKPixelGeometry.RgbHorizontal));
                        surface.Canvas.Scale((float)dpiX, (float)dpiY);
                        surface.Canvas.Save();

                        if (preRender) {
                            bitmap.Lock();

                            var canvas = surface.Canvas;
                            using (new SKAutoCanvasRestore(surface.Canvas, true)) {
                                // Traverse transparent background color tree
                                EtoControl? currWidget = drawable;
                                EtoColor bgColor = drawable.BackgroundColor;
                                while (bgColor.A <= 0.01f && currWidget != null) {
                                    currWidget = currWidget.Parent;
                                    bgColor = currWidget?.BackgroundColor ?? EtoColors.Transparent;
                                }

                                if (bgColor.A <= 0.01f) {
                                    // Use native background color
                                    canvas.Clear(Settings.Instance.Theme.DarkMode ? Settings.Instance.Theme.Background.ToSkia() : SKColors.White);
                                } else {
                                    canvas.Clear(bgColor.ToSkia());
                                }

                                drawable.Draw(surface);
                            }
                            canvas.Flush();

                            bitmap.AddDirtyRect(new Int32Rect(0, 0, imageWidth, imageHeight));
                            bitmap.Unlock();
                        }
                    }
                }

                if (preRender) {
                    if (bitmap == null || drawable.DrawWidth <= 0 || drawable.DrawHeight <= 0) return;

                    var srcRect = new Int32Rect(drawable.DrawX + drawable.Padding.Left, drawable.DrawY + drawable.Padding.Top, (int)(drawable.DrawWidth * dpiX), (int)(drawable.DrawHeight * dpiY));
                    srcRect.X = Math.Min(srcRect.X, imageWidth - 1);
                    srcRect.Y = Math.Min(srcRect.Y, imageHeight - 1);
                    srcRect.Width = Math.Min(srcRect.Width, imageWidth - srcRect.X);
                    srcRect.Height = Math.Min(srcRect.Height, imageHeight - srcRect.Y);

                    var croppedBitmap = new CroppedBitmap(bitmap, srcRect);

                    drawingContext.DrawImage(croppedBitmap, new Rect(drawable.DrawX + drawable.Padding.Left, drawable.DrawY + drawable.Padding.Top, drawable.DrawWidth / (dpiX), drawable.DrawHeight / (dpiY)));
                } else {
                    bitmap?.Lock();

                    var canvas = surface.Canvas;
                    using (new SKAutoCanvasRestore(surface.Canvas, true)) {
                        // Traverse transparent background color tree
                        EtoControl? currWidget = drawable;
                        EtoColor bgColor = drawable.BackgroundColor;
                        while (bgColor.A <= 0.01f && currWidget != null) {
                            currWidget = currWidget.Parent;
                            bgColor = currWidget?.BackgroundColor ?? EtoColors.Transparent;
                        }

                        if (bgColor.A <= 0.01f) {
                            // Use native background color
                            canvas.Clear(Settings.Instance.Theme.DarkMode ? Settings.Instance.Theme.Background.ToSkia() : SKColors.White);
                        } else {
                            canvas.Clear(bgColor.ToSkia());
                        }

                        canvas.Translate(-drawable.DrawX, -drawable.DrawY);
                        drawable.Draw(surface);
                    }
                    canvas.Flush();

                    if (bitmap != null) {
                        bitmap.AddDirtyRect(new Int32Rect(0, 0, imageWidth, imageHeight));
                        drawingContext.DrawImage(bitmap, new Rect(drawable.DrawX + drawable.Padding.Left, drawable.DrawY + drawable.Padding.Top, imageWidth / dpiX, imageHeight / dpiY));
                        bitmap.Unlock();
                    }
                }
            } else {
                drawable.Invalidate();
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

