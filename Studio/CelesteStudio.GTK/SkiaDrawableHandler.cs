using Cairo;
using CelesteStudio.Controls;
using CelesteStudio.Util;
using Eto.Forms;
using Eto.GtkSharp.Forms;
using SkiaSharp;
using System;

namespace CelesteStudio.GTK;

public class SkiaDrawableHandler : GtkPanel<Gtk.EventBox, SkiaDrawable, Control.ICallback>, SkiaDrawable.IHandler {
    private Gtk.Box content = null!;

    protected override WeakConnector CreateConnector() => new SkiaDrawableConnector();
    protected new SkiaDrawableConnector Connector => (SkiaDrawableConnector)base.Connector;

    public void Create() {
        Control = new Gtk.EventBox();
        Control.Events |= Gdk.EventMask.ExposureMask;
        Control.CanFocus = false;
        Control.CanDefault = true;
        Control.Events |= Gdk.EventMask.ButtonPressMask;

        content = new Gtk.Box(Gtk.Orientation.Vertical, 0);
        Control.Add(content);
    }

    protected override void Initialize() {
        base.Initialize();
        Control.Drawn += Connector.HandleDrawn;
        Control.ButtonPressEvent += Connector.HandleDrawableButtonPressEvent;
    }

    protected class SkiaDrawableConnector : GtkPanelEventConnector {
        private new SkiaDrawableHandler? Handler => (SkiaDrawableHandler)base.Handler;

        private SKBitmap? bitmap;
        private SKSurface? surface;
        private ImageSurface? imageSurface;

        public void HandleDrawableButtonPressEvent(object o, Gtk.ButtonPressEventArgs args) {
            var handler = Handler;
            if (handler == null) {
                return;
            }

            if (handler.CanFocus) {
                handler.Control.GrabFocus();
            }
        }

        [GLib.ConnectBefore]
        public void HandleDrawn(object o, Gtk.DrawnArgs args) {
            if (Handler == null) {
                return;
            }

            var drawable = Handler.Widget;
            bool preRender = drawable.PreRenderImage;
            if (drawable.CanDraw) {
                int width = drawable.ImageWidth, height = drawable.ImageHeight;
                if (surface == null || imageSurface == null || width != bitmap?.Width || height != bitmap?.Height) {
                    var colorType = SKImageInfo.PlatformColorType;

                    bitmap?.Dispose();
                    bitmap = new SKBitmap(width, height, colorType, SKAlphaType.Premul);
                    IntPtr pixels = bitmap.GetPixels();

                    surface?.Dispose();
                    surface = SKSurface.Create(new SKImageInfo(bitmap.Info.Width, bitmap.Info.Height, colorType, SKAlphaType.Premul), pixels, bitmap.Info.RowBytes);
                    surface.Canvas.Flush();

                    imageSurface?.Dispose();
                    imageSurface = new ImageSurface(pixels, Format.Argb32, bitmap.Width, bitmap.Height, bitmap.Width * 4);

                    if (preRender) {
                        var canvas = surface.Canvas;
                        using (new SKAutoCanvasRestore(canvas, true)) {
                            canvas.Clear(drawable.BackgroundColor.ToSkia());
                            drawable.Draw(surface);
                        }
                    }
                }

                if (!preRender) {
                    var canvas = surface.Canvas;
                    using (new SKAutoCanvasRestore(canvas, true)) {
                        canvas.Clear(drawable.BackgroundColor.ToSkia());
                        canvas.Translate(-drawable.DrawX, -drawable.DrawY);
                        drawable.Draw(surface);
                    }
                }
            } else {
                drawable.Invalidate();
            }

            if (imageSurface != null) {
                if (preRender) {
                    args.Cr.SetSourceSurface(imageSurface, drawable.Padding.Left, drawable.Padding.Top);
                } else {
                    args.Cr.SetSourceSurface(imageSurface, drawable.DrawX + drawable.Padding.Left, drawable.DrawY + drawable.Padding.Top);
                }
                args.Cr.Paint();
            }
        }
    }

    public bool CanFocus {
        get => Control.CanFocus;
        set => Control.CanFocus = value;
    }

    protected override void SetContainerContent(Gtk.Widget containerContent) {
        content.Add(containerContent);
    }

    protected override void SetBackgroundColor(Eto.Drawing.Color? color) {
        // Handled by ourselves
        Invalidate(false);
    }
}
