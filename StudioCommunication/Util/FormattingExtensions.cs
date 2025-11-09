using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace StudioCommunication.Util;

public static class FormattingExtensions {
    /// Unify all TASes to use a single line separator
    public const char NewLine = '\n';

    /// Formats lines of a file into a single string, using consistent formatting rules
    public static string FormatTasLinesToText(this IEnumerable<string> lines) {
        return string.Join("", lines
            // Trim leading empty lines
            .SkipWhile(string.IsNullOrWhiteSpace)
            // Trim trailing empty lines
            .Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse()
            .Select(line => {
                if (ActionLine.TryParse(line, out var actionLine)) {
                    return $"{actionLine}{NewLine}";
                }

                // Trim whitespace and remove invalid characters
                return new string(line.Trim().Where(c => !char.IsControl(c) && c != char.MaxValue).ToArray()) + $"{NewLine}";
            }));
    }

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

#if NET7_0_OR_GREATER
    private static readonly string[] sizeSuffixes = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];
    public static (string Amount, string Suffix) HumanReadableBytes<T>(this T value, int decimals = 1) where T : INumber<T>
    {
        if (value < T.Zero) {
            (string amount, string suffix) = HumanReadableBytes(-value, decimals);
            return ("-" + amount, suffix);
        }
        if (value == T.Zero) {
            return (string.Format($"{{0:n{decimals}}}", 0), sizeSuffixes[0]);
        }

        // mag is 0 for bytes, 1 for KiB, 2, for MiB, etc.
        int mag = (int)Math.Log(double.CreateChecked(value), 1024);

        // 1L << (mag * 10) == 2 ^ (10 * mag)
        // (i.e. the number of bytes in the unit corresponding to mag)
        decimal adjustedSize = decimal.CreateChecked(value) / (1L << (mag * 10));

        // Make adjustment when the value is large enough that it would round up to 1000 or more
        if (Math.Round(adjustedSize, decimals) >= 1000)
        {
            mag += 1;
            adjustedSize /= 1024;
        }

        return (string.Format($"{{0:n{decimals}}}", adjustedSize), sizeSuffixes[mag]);
    }
#endif

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
