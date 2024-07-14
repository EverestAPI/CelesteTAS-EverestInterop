using Eto.Drawing;

namespace CelesteStudio;

public static class Assets {
    
    public static Icon AppIcon = Icon.FromResource("Icon.ico");
    
    public static readonly IGraphicsPath CollapseOpenPath = CreateVectorPath(new(0.0f, 0.0f), new(1.0f, 0.0f), new(0.5f, 1.0f));
    public static readonly IGraphicsPath CollapseClosedPath = CreateVectorPath(new(0.0f, 0.0f), new(1.0f, 0.5f), new(0.0f, 1.0f));
        
    private static IGraphicsPath CreateVectorPath(params PointF[] points) {
        var path = GraphicsPath.Create();
        path.AddLines(points);
        return path;
    } 
}