using Eto;
using Eto.Drawing;
using Eto.Forms;
using SkiaSharp;
using System;

namespace CelesteStudio.Controls;

public abstract class SkiaDrawable : Drawable {
    private readonly SKColorType colorType = Platform.Instance.IsWinForms || Platform.Instance.IsWpf ? SKColorType.Bgra8888 : SKColorType.Rgba8888;

    private Bitmap? image = null;
    private SKImageInfo imageInfo = SKImageInfo.Empty;

    protected virtual int DrawX => 0;
    protected virtual int DrawY => 0;
    protected virtual int DrawWidth => Width;
    protected virtual int DrawHeight => Height;

    protected abstract void Draw(PaintEventArgs e, SKSurface surface, SKImageInfo info);

    protected override void OnPaint(PaintEventArgs e)
    {
        try {
            int drawWidth = DrawWidth, drawHeight = DrawHeight;

            if (drawWidth <= 0 || drawHeight <= 0) {
                return;
            }

            if (drawWidth != image?.Size.Width || drawHeight != image?.Size.Height)
            {
                image?.Dispose();
                image = new Bitmap(new Size(drawWidth, drawHeight), PixelFormat.Format32bppRgba);
                imageInfo = new SKImageInfo(drawWidth, drawHeight, colorType, SKAlphaType.Unpremul);
            }

            using var bmp = image.Lock();
            using var surface = SKSurface.Create(imageInfo, bmp.Data, bmp.ScanWidth);

            Draw(e, surface, imageInfo);

            e.Graphics.DrawImage(image, DrawX, DrawY);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            e.Graphics.DrawText(Fonts.Monospace(12.0f), Colors.Red, PointF.Empty, ex.ToString());
        }
    }
}
