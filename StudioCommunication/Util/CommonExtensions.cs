using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace StudioCommunication.Util;

/// Splits each line into its own slice, accounting for LF, CRLF and CR line endings
public ref struct LineIterator(ReadOnlySpan<char> text) {
    private ReadOnlySpan<char> text = text;
    private int startIdx = 0;

    public ReadOnlySpan<char> Current { get; private set; }
    public LineIterator GetEnumerator() => this;

    public bool MoveNext() {
        for (int i = startIdx; i < text.Length; i++) {
            // \n is always a newline
            if (text[i] == '\n') {
                Current = text[startIdx..i];
                startIdx = i + 1;
                return true;
            }

            // \r is either alone or a \r\n
            if (text[i] == '\r') {
                Current = text[startIdx..i];

                if (i + 1 < text.Length && text[i + 1] == '\n') {
                    i++;
                }

                startIdx = i + 1;
                return true;
            }
        }

        if (startIdx != text.Length) {
            Current =  text[startIdx..];
            startIdx = text.Length;
            return true;
        }

        return false;
    }
}

#if NET7_0_OR_GREATER
public static class NumberExtensions {
    public static T Mod<T>(this T x, T m) where T : INumber<T> => (x % m + m) % m;
}
#endif

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

    /// Counts the amount of lines, accounting for LF, CRLF and CR line endings
    public static int CountLines(this string str) {
        int lines = 1;

        for (int i = 0; i < str.Length; i++) {
            // \n is always a newline
            if (str[i] == '\n') {
                lines++;
                continue;
            }

            // \r is either alone or a \r\n
            if (str[i] == '\r') {
                lines++;
                if (i + 1 < str.Length && str[i + 1] == '\n') {
                    i++;
                }
            }
        }

        return lines;
    }

    /// Splits each line into its own string, accounting for LF, CRLF and CR line endings
    public static IEnumerable<string> SplitLines(this string str) {
        int startIdx = 0;
        for (int i = 0; i < str.Length; i++) {
            // \n is always a newline
            if (str[i] == '\n') {
                yield return str[startIdx..i];
                startIdx = i + 1;
                continue;
            }

            // \r is either alone or a \r\n
            if (str[i] == '\r') {
                yield return str[startIdx..i];

                if (i + 1 < str.Length && str[i + 1] == '\n') {
                    i++;
                }

                startIdx = i + 1;
            }
        }

        if (startIdx != str.Length) {
            yield return str[startIdx..];
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

public static class EnumExtensions {
#if NET7_0_OR_GREATER
    /// Split a flags enum into the individual flags
    public static unsafe IEnumerable<T> ToValues<T>(this T flags) where T : unmanaged, Enum {
        return sizeof(T) switch {
            1 => ToValuesInternal<T, byte>(flags),
            2 => ToValuesInternal<T, ushort>(flags),
            4 => ToValuesInternal<T, uint>(flags),
            8 => ToValuesInternal<T, ulong>(flags),
            _ => throw new Exception("Size does not match a known Enum backing type."),
        };
    }

    private static IEnumerable<TEnum> ToValuesInternal<TEnum, TBacking>(this TEnum flags) where TEnum : unmanaged, Enum where TBacking : unmanaged, INumber<TBacking>, IBitwiseOperators<TBacking, TBacking, TBacking>, IEqualityOperators<TBacking, TBacking, bool> {
        var flagsInt = Unsafe.As<TEnum, TBacking>(ref flags);
        foreach (object? valueObject in Enum.GetValues(typeof(TEnum))) {
            var value = (TEnum) valueObject;
            var valueInt = Unsafe.As<TEnum, TBacking>(ref value);
            if ((valueInt & flagsInt) != TBacking.CreateChecked(0)) {
                yield return value;
            }
        }
    }
#endif
}

public static class DictionaryExtensions {
    /// Adds an element to the list stored under the specified key
    public static void AddToKey<TKey, TValue>(this IDictionary<TKey, List<TValue>> dict, TKey key, TValue value) {
        if (dict.TryGetValue(key, out var list)) {
            list.Add(value);
            return;
        }
        dict[key] = [value];
    }
    /// Adds all elements to the list stored under the specified key
    public static void AddRangeToKey<TKey, TValue>(this IDictionary<TKey, List<TValue>> dict, TKey key, IEnumerable<TValue> values) {
        if (dict.TryGetValue(key, out var list)) {
            list.AddRange(values);
            return;
        }
        dict[key] = [..values];
    }
}

public static class StackExtensions {
    /// Pushes a range of elements onto the queue, in order
    public static void PushRange<T>(this Stack<T> stack, IEnumerable<T> items) {
        switch (items) {
            case IList<T> list: {
#if NET7_0_OR_GREATER
                stack.EnsureCapacity(stack.Count + list.Count);
#endif
                for (int i = 0; i < list.Count; i++) {
                    stack.Push(list[i]);
                }
                break;
            }
            case ICollection<T> collection: {
#if NET7_0_OR_GREATER
                stack.EnsureCapacity(stack.Count + collection.Count);
#endif
                foreach (var item in collection) {
                    stack.Push(item);
                }
                break;
            }
            default: {
                foreach (var item in items) {
                    stack.Push(item);
                }
                break;
            }
        }
    }
}
