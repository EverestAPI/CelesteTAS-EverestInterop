using Eto.Drawing;
using SkiaSharp;
using System;

namespace CelesteStudio;

public static class Assets {

    public static Icon AppIcon = Icon.FromResource("Icon.ico");

    public static readonly SKPath CollapseOpenPath = CreateVectorPath(new(0.0f, 0.0f), new(1.0f, 0.0f), new(0.5f, 1.0f));
    public static readonly SKPath CollapseClosedPath = CreateVectorPath(new(0.0f, 0.0f), new(1.0f, 0.5f), new(0.0f, 1.0f));

    public static IGraphicsPath PopoutPath => CreatePopoutPath();

    public static SKPath FavouritePath => CreateFavouritePath();
    public static SKPath FrequentlyUsedPath => CreateFrequentlyUsedPath();
    public static SKPath SuggestionPath => CreateSuggestionPath();

    private static SKPath CreateVectorPath(params SKPoint[] points) {
        var path = new SKPath();
        path.AddPoly(points, close: true);
        return path;
    }
    private static IGraphicsPath CreatePopoutPath() {
        var path = GraphicsPath.Create();

        path.AddLines([new(0.6f, 0.1f), new(0.9f, 0.1f), new(0.9f, 0.4f), new(0.8f, 0.4f), new(0.8f, 0.25625f), new(0.535417f, 0.53541f), new(0.464584f, 0.464584f), new(0.72917f, 0.2f), new(0.6f, 0.2f)]);
        path.StartFigure();
        path.AddLines([new(0.8f, 0.8f), new(0.8f, 0.5f), new(0.7f, 0.5f), new(0.7f, 0.8f), new(0.2f, 0.8f), new(0.2f, 0.3f), new(0.5f, 0.3f), new(0.5f, 0.2f), new(0.2f, 0.2f)]);
        path.AddArc(path.CurrentPoint.X - 0.1f, path.CurrentPoint.Y, 0.1f, 0.1f, 270.0f, -90.0f);
        path.AddLine(path.CurrentPoint.X, path.CurrentPoint.Y, 0.1f, 0.8f);
        path.AddArc(path.CurrentPoint.X, path.CurrentPoint.Y, 0.1f, 0.1f, 180.0f, -90.0f);
        path.AddLine(path.CurrentPoint.X, path.CurrentPoint.Y, 0.7f, 0.9f);
        path.AddArc(path.CurrentPoint.X, path.CurrentPoint.Y - 0.1f, 0.1f, 0.1f, 90.0f, -90.0f);

        return path;
    }

    private static SKPath CreateFavouritePath() {
        var path = new SKPath();

        const float viewWidth = 32.0f;
        const float viewHeight = 32.0f;

        const float centerX = viewWidth / 2.0f;
        const float bottomY = viewHeight - 4.0f;

        const float bottomRoundingW = 3.0f;
        const float bottomRoundingH = 3.0f;
        const float bottomControlLength = 2.5f;
        path.MoveTo(centerX + bottomRoundingW, bottomY - bottomRoundingH);
        path.CubicTo(
            x0: centerX + bottomRoundingW - bottomControlLength, y0: bottomY - bottomRoundingH + bottomControlLength,
            x1: centerX - bottomRoundingW + bottomControlLength, y1: bottomY - bottomRoundingH + bottomControlLength,
            x2: centerX - bottomRoundingW, y2: bottomY - bottomRoundingH);

        const float sideLength = 6.0f;
        path.RLineTo(-sideLength, -sideLength);

        const float rootTwo = 1.4142135623730951f;
        const float outerDist = 3f * rootTwo;
        const float controlPointFactor = 1.5f;
        path.RCubicTo(-outerDist * controlPointFactor, -outerDist * controlPointFactor, outerDist*2 - outerDist * controlPointFactor, -outerDist*2 - outerDist * controlPointFactor, outerDist*2, -outerDist*2);

        float middleDist = centerX - path.LastPoint.X;
        path.RLineTo(middleDist,  middleDist);
        path.RLineTo(middleDist, -middleDist);

        path.RCubicTo(outerDist * controlPointFactor, -outerDist * controlPointFactor, outerDist*2 + outerDist * controlPointFactor, outerDist*2 - outerDist * controlPointFactor, outerDist*2, outerDist*2);
        path.RLineTo(-sideLength, sideLength);

        path.Transform(SKMatrix.CreateScale(1.0f / viewWidth, 1.0f / viewHeight));

        return path;
    }
    private static SKPath CreateFrequentlyUsedPath() {
        var path = new SKPath();

        const float viewWidth = 32.0f;
        const float viewHeight = 32.0f;

        const float innerRadius = 5.0f;
        const float outerRadius = 10.0f;

        path.MoveTo(viewWidth / 2.0f, viewHeight / 2.0f - outerRadius);
        for (int i = 1; i < 10; i ++) {
            (float currRadius, float nextRadius) = i % 2 == 0 ? (outerRadius, innerRadius) : (innerRadius, outerRadius);
            float angle = MathF.Tau / 10.0f * i - MathF.PI / 2.0f;

            (float sin, float cos) = MathF.SinCos(angle);
            path.LineTo(viewWidth / 2.0f + cos * currRadius, viewHeight / 2.0f + sin * currRadius);
        }
        path.Close();

        path.Transform(SKMatrix.CreateScale(1.0f / viewWidth, 1.0f / viewHeight));

        return path;
    }
    private static SKPath CreateSuggestionPath() {
        var path = new SKPath();

        const float viewWidth = 32.0f;
        const float viewHeight = 32.0f;

        const float centerX = viewWidth / 2.0f;
        const float bottomY = viewHeight - 4.0f;

        const float bottomHeight = 3.0f;
        const float bottomWidth = 12.0f;
        const float bottomControlFactor = 0.75f;
        const float bottomGap = 3.0f;
        path.MoveTo(centerX - bottomWidth / 2.0f, bottomY - bottomHeight);
        path.RCubicTo(0.0f, bottomHeight * bottomControlFactor, bottomWidth, bottomHeight * bottomControlFactor, bottomWidth, 0.0f);

        const float screwWidth = 14.0f;
        const float screwGap = 4.0f;
        path.RMoveTo(-bottomWidth / 2.0f - screwWidth / 2.0f, -bottomGap);
        path.RLineTo(screwWidth, 0.0f);

        const float baseWidth = 11.0f;
        const float baseHeight = 4.0f;
        path.RMoveTo(-screwWidth + (screwWidth - baseWidth) / 2.0f, -screwGap);
        path.RLineTo(0.0f, -baseHeight);

        const float bulbAngle = 35.0f;
        const float bulbStartControlDist = 5.0f;
        const float bulbEndControlDist = 10.0f;
        const float bulbHeight = 13.0f;
        (float bulbSin, float bulbCos) = MathF.SinCos(bulbAngle * (MathF.PI / 180.0f));
        path.RCubicTo(
            -bulbCos * bulbStartControlDist, -bulbSin * bulbStartControlDist,
            baseWidth / 2.0f - bulbEndControlDist, -bulbHeight,
            baseWidth / 2.0f, -bulbHeight);
        path.RCubicTo(
            bulbEndControlDist, 0.0f,
            baseWidth / 2.0f + bulbCos * bulbStartControlDist, bulbHeight - bulbSin * bulbStartControlDist,
            baseWidth / 2.0f, bulbHeight);

        path.RLineTo(0.0f, baseHeight);
        path.RLineTo(-baseWidth, 0.0f);

        const float wireOffset = 6.0f;
        const float wireAngle = 55.0f;
        const float wireLength = 5.0f;
        (float wireSin, float wireCos) = MathF.SinCos(wireAngle * (MathF.PI / 180.0f));
        path.RMoveTo(baseWidth / 2.0f, -wireOffset);
        path.RLineTo(-wireCos * wireLength, -wireSin * wireLength);
        path.RMoveTo(wireCos * wireLength * 2.0f, 0.0f);
        path.RLineTo(-wireCos * wireLength, wireSin * wireLength);

        path.Transform(SKMatrix.CreateScale(1.0f / viewWidth, 1.0f / viewHeight));

        return path;
    }
}
