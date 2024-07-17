using Eto.Drawing;

namespace CelesteStudio;

public static class Assets {
    
    public static Icon AppIcon = Icon.FromResource("Icon.ico");
    
    public static readonly IGraphicsPath CollapseOpenPath = CreateVectorPath(new(0.0f, 0.0f), new(1.0f, 0.0f), new(0.5f, 1.0f));
    public static readonly IGraphicsPath CollapseClosedPath = CreateVectorPath(new(0.0f, 0.0f), new(1.0f, 0.5f), new(0.0f, 1.0f));
        
    public static IGraphicsPath PopoutPath => CreatePopoutPath();
    
    private static IGraphicsPath CreateVectorPath(params PointF[] points) {
        var path = GraphicsPath.Create();
        path.AddLines(points);
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
}