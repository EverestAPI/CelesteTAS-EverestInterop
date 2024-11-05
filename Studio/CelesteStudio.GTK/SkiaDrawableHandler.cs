using Cairo;
using CelesteStudio.Controls;
using Eto.GtkSharp.Forms;
using SkiaSharp;
using SkiaSharp.Views.Gtk;
using System;

namespace CelesteStudio.GTK;

public class SkiaDrawableHandler : GtkPanel<Gtk.EventBox, SkiaDrawable, SkiaDrawable.ICallback>, SkiaDrawable.IHandler {
    private Gtk.Box content = null!;

    public void Create() {
        Control = new SkiaEventBox(Widget);
        Control.Events |= Gdk.EventMask.ExposureMask;
        Control.CanFocus = false;
        Control.CanDefault = true;
        Control.Events |= Gdk.EventMask.ButtonPressMask;

        content = new Gtk.Box(Gtk.Orientation.Vertical, 0);
        Control.Add(content);
    }

    private class SkiaEventBox(SkiaDrawable drawable) : Gtk.EventBox {
        private SKBitmap? bitmap;
        private SKSurface? surface;
        private ImageSurface? imageSurface;

        protected override bool OnDrawn(Context cr) {
            if (base.OnDrawn(cr)) {
                return true;
            }

            int width = drawable.DrawWidth, height = drawable.DrawHeight;
            if (width != bitmap?.Width || height != bitmap?.Height) {
                var colorType = SKImageInfo.PlatformColorType;

                bitmap?.Dispose();
                bitmap = new SKBitmap(width, height, colorType, SKAlphaType.Premul);
                IntPtr pixels = bitmap.GetPixels();

                surface?.Dispose();
                surface = SKSurface.Create(new SKImageInfo(bitmap.Info.Width, bitmap.Info.Height, colorType, SKAlphaType.Premul), pixels, bitmap.Info.RowBytes);
                surface.Canvas.Flush();

                imageSurface?.Dispose();
                imageSurface = new ImageSurface(pixels, Format.Argb32, bitmap.Width, bitmap.Height, bitmap.Width * 4);
            }

            using (new SKAutoCanvasRestore(surface!.Canvas, true)) {
                drawable.Draw(surface);
            }

            imageSurface!.MarkDirty();
            cr.SetSourceSurface(imageSurface, drawable.DrawX, drawable.DrawY);
            cr.Paint();

            return false;
        }
    }

    public bool CanFocus {
        get => Control.CanFocus;
        set => Control.CanFocus = value;
    }

    protected override void SetContainerContent(Gtk.Widget containerContent)
    {
        content.Add(containerContent);
    }
}
