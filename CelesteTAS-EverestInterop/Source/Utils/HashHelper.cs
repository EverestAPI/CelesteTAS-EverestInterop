using System.Text;
using Celeste.Mod;

namespace TAS.Utils;

internal static class HashHelper {
    public static string ComputeHash(string text) {
        return XXHash64.Create().ComputeHash(Encoding.UTF8.GetBytes(text)).ToHexadecimalString();
    }
}
