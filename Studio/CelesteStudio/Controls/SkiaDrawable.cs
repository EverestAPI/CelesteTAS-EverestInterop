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

    protected abstract void Draw(PaintEventArgs e, SKSurface surface, SKImageInfo info);

    protected override void OnPaint(PaintEventArgs e)
    {
        try {
            if (Width <= 0 || Height <= 0) {
                return;
            }

            if (Size != image?.Size)
            {
                image?.Dispose();
                image = new Bitmap(Size, PixelFormat.Format32bppRgba);
                imageInfo = new SKImageInfo(Width, Height, colorType, SKAlphaType.Unpremul);
            }

            using var bmp = image.Lock();
            using var surface = SKSurface.Create(imageInfo, bmp.Data, bmp.ScanWidth);

            Draw(e, surface, imageInfo);

            e.Graphics.DrawImage(image, PointF.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            e.Graphics.DrawText(Fonts.Monospace(12.0f), Colors.Red, PointF.Empty, ex.ToString());
        }
    }
}
