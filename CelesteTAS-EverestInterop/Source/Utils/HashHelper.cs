using System.Text;
using Celeste.Mod;

namespace TAS.Utils;

internal static class HashHelper {
    public static string ComputeHash(string text) {
        return Everest.ChecksumHasher.ComputeHash(Encoding.UTF8.GetBytes(text)).ToHexadecimalString();
    }
}