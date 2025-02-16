using TAS.Utils;

namespace TAS.InfoHUD;

/// Provides information about the current mouse cursor
/// Additionally controls the placement of the in-game Info HUD
internal static class InfoMouse {
    public static readonly LazyValue<string> Info = new(QueryInfo);

    private static string QueryInfo() {
        return string.Empty;
    }
}
