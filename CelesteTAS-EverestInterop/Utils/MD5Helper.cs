using System.Security.Cryptography;
using System.Text;
using Celeste.Mod;

namespace TAS {
    internal static class Md5Helper {
        private static readonly MD5 ChecksumHasher = MD5.Create();

        public static string ComputeHash(string text) {
            return ChecksumHasher.ComputeHash(Encoding.UTF8.GetBytes(text)).ToHexadecimalString();
        }
    }
}