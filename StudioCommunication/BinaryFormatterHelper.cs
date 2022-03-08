using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace StudioCommunication;

public static class BinaryFormatterHelper {
    private static readonly BinaryFormatter BinaryFormatter = new();

    //ty stackoverflow
    public static T FromByteArray<T>(byte[] data, int offset = 0, int length = 0) {
        if (data == null) {
            return default(T);
        }

        if (length == 0) {
            length = data.Length - offset;
        }

        using MemoryStream ms = new(data, offset, length);
        object obj = BinaryFormatter.Deserialize(ms);
        return (T) obj;
    }

    public static byte[] ToByteArray<T>(T obj) {
        if (obj == null) {
            return new byte[0];
        }

        using MemoryStream ms = new();
        BinaryFormatter.Serialize(ms, obj);
        return ms.ToArray();
    }
}