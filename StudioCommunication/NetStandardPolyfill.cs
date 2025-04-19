#if !NET7_0_OR_GREATER
using System;
using System.IO;
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

namespace StudioCommunication {
    public static class NetStandardPolyfill {
        // https://github.com/microsoft/referencesource/blob/51cf7850defa8a17d815b4700b67116e3fa283c2/mscorlib/system/io/binarywriter.cs#L414
        public static void Write7BitEncodedInt(this BinaryWriter writer, int value) {
            // Write out an int 7 bits at a time.  The high bit of the byte,
            // when on, tells reader to continue reading more bytes.
            uint v = (uint) value;   // support negative numbers
            while (v >= 0x80) {
                writer.Write((byte) (v | 0x80));
                v >>= 7;
            }
            writer.Write((byte)v);
        }
        
        // https://github.com/microsoft/referencesource/blob/51cf7850defa8a17d815b4700b67116e3fa283c2/mscorlib/system/io/binaryreader.cs#L582
        public static int Read7BitEncodedInt(this BinaryReader writer) {
            // Read out an Int32 7 bits at a time.  The high bit
            // of the byte when on means to continue reading more bytes.
            int count = 0;
            int shift = 0;
            byte b;
            do {
                // Check for a corrupted stream.  Read a max of 5 bytes.
                // In a future version, add a DataFormatException.
                if (shift == 5 * 7)  // 5 bytes max per Int32, shift += 7
                    throw new FormatException("Format_Bad7BitInt32");

                // ReadByte handles end of stream cases for us.
                b = writer.ReadByte();
                count |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return count;
        }
    }
}
#endif

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices {
    internal class IsExternalInit;
}
#endif

#if !NET8_0_OR_GREATER
namespace System.Runtime.CompilerServices {
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    internal sealed class RequiredMemberAttribute : Attribute;
    
    // https://stackoverflow.com/a/75995697
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute {
        public string FeatureName { get; } = featureName;
        public bool IsOptional { get; init; }
        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }
}
#endif
