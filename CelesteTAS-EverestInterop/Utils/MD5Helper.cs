using System.Security.Cryptography;
using System.Text;
using Celeste.Mod;

namespace TAS {
internal static class MD5Helper {
    private static readonly MD5 checksumHasher = MD5.Create();

    public static string ComputeHash(string text) {
        return checksumHasher.ComputeHash(Encoding.UTF8.GetBytes(text)).ToHexadecimalString();
    }
}
}