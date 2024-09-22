using System;
using System.Collections.Generic;
using System.Linq;

namespace StudioCommunication.Util;

public static class StringExtensions {
    private static readonly string format = "0.".PadRight(339, '#');
    public static string ToFormattedString(this float value, int decimals) {
        if (decimals == 0) {
            return value.ToString(format);
        } else {
            return ((double) value).ToFormattedString(decimals);
        }
    }
    public static string ToFormattedString(this double value, int decimals) {
        if (decimals == 0) {
            return value.ToString(format);
        } else {
            return value.ToString($"F{decimals}");
        }
    }

    /// Replaces the specified range inside the string and returns the result
    public static string ReplaceRange(this string self, int startIndex, int count, string replacement) {
        return self.Remove(startIndex, count).Insert(startIndex, replacement);
    }

    /// A stable (consistent) hash code for a specific string
    public static int GetStableHashCode(this string str)
    {
        // Taken from https://stackoverflow.com/a/36845864
        unchecked {
            int hash1 = 5381;
            int hash2 = hash1;

            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2) {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1 || str[i+1] == '\0') {
                    break;
                }
                hash2 = ((hash2 << 5) + hash2) ^ str[i+1];
            }

            return hash1 + (hash2*1566083941);
        }
    }
}

public static class TypeExtensions {
    private static readonly Dictionary<Type, string> shorthandMap = new() {
        { typeof(bool), "bool" },
        { typeof(byte), "byte" },
        { typeof(char), "char" },
        { typeof(decimal), "decimal" },
        { typeof(double), "double" },
        { typeof(float), "float" },
        { typeof(int), "int" },
        { typeof(long), "long" },
        { typeof(sbyte), "sbyte" },
        { typeof(short), "short" },
        { typeof(string), "string" },
        { typeof(uint), "uint" },
        { typeof(ulong), "ulong" },
        { typeof(ushort), "ushort" },
    };
    public static string CSharpName(this Type type, bool isOut = false) {
        if (type.IsByRef) {
            return $"{(isOut ? "out" : "ref")} {type.GetElementType()!.CSharpName()}";
        }
        if (type.IsGenericType) {
            if (type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                return $"{Nullable.GetUnderlyingType(type)!.CSharpName()}?";
            }
            return $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GenericTypeArguments.Select(a => a.CSharpName()).ToArray())}>";
        }
        if (type.IsArray) {
            return $"{type.GetElementType()!.CSharpName()}[]";
        }

        if (shorthandMap.TryGetValue(type, out string? shorthand)) {
            return shorthand;
        }
        if (type.FullName == null) {
            return type.Name;
        }

        int namespaceLen = type.Namespace != null
            ? type.Namespace.Length + 1
            : 0;
        return type.FullName[namespaceLen..];
    }
}
