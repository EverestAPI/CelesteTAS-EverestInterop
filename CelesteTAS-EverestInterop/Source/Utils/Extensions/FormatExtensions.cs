using Microsoft.Xna.Framework;

namespace TAS.Utils;

/// Helper functions for formatting various values to simple human-readable string
internal static class FormatExtensions {
    private static readonly string fullPrecision = "0.".PadRight(339, '#');

    public static string FormatValue(this float value,  int decimals) => value.ToString(decimals == 0 ? fullPrecision : $"F{decimals}");
    public static string FormatValue(this double value, int decimals) => value.ToString(decimals == 0 ? fullPrecision : $"F{decimals}");
    public static string FormatValue(this Vector2 vec, int decimals) => $"{vec.X.FormatValue(decimals)}, {vec.Y.FormatValue(decimals)}";
}
