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

public interface IStableHash {
    /// Computes a stable hash-code for the current object state
    public int GetStableHashCode();
}

public static class HashExtensions {
    public static int GetStableHashCode<T>(this T value) {
        if (typeof(T).IsPrimitive) {
            return value!.GetHashCode(); // Primitives return their bit-representation as hashcode
        }
        if (value is null) {
            return 0;
        }

        if (typeof(T) == typeof(string)) {
            return ((string)(object) value).GetStableHashCode();
        }
        if (typeof(T).IsSubclassOf(typeof(IStableHash))) {
            return ((IStableHash) value).GetStableHashCode();
        }

        throw new NotImplementedException($"Cannot create stable hash of type '{typeof(T)}'");
    }

    /// A stable (consistent) hash code for a specific string
    public static int GetStableHashCode(this string str) {
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

public static class StringExtensions {
    /// Replaces the specified range inside the string and returns the result
    public static string ReplaceRange(this string self, int startIndex, int count, string replacement) {
        return self.Remove(startIndex, count).Insert(startIndex, replacement);
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

public static class CollectionExtensions {
    /// Find the index of the first matching element. Otherwise -1
    public static int IndexOf<T>(this T[] array, Func<T, bool> predicate) {
        for (int i = 0; i < array.Length; i++) {
            if (predicate(array[i])) {
                return i;
            }
        }

        return -1;
    }

    /// Find the index of the first matching element. Otherwise -1
    public static int IndexOf<T>(this IList<T> list, Func<T, bool> predicate) {
        for (int i = 0; i < list.Count; i++) {
            if (predicate(list[i])) {
                return i;
            }
        }

        return -1;
    }

    // Median implementation based on https://stackoverflow.com/a/22702269

    /// <summary>
    /// Note: specified list would be mutated in the process.
    /// </summary>
    public static T Median<T>(this IList<T> list) where T : IComparable<T> {
        return list.NthOrderStatistic((list.Count - 1)/2);
    }

    public static T Median<T>(this IEnumerable<T> sequence) where T : IComparable<T> {
        var list = sequence.ToList();
        int mid = (list.Count - 1) / 2;
        return list.NthOrderStatistic(mid);
    }

    /// <summary>
    /// Partitions the given list around a pivot element such that all elements on left of pivot are <= pivot
    /// and the ones at thr right are > pivot. This method can be used for sorting, N-order statistics such as
    /// as median finding algorithms.
    /// Pivot is selected ranodmly if random number generator is supplied else its selected as last element in the list.
    /// Reference: Introduction to Algorithms 3rd Edition, Corman et al, pp 171
    /// </summary>
    private static int Partition<T>(this IList<T> list, int start, int end, Random? rnd = null) where T : IComparable<T> {
        if (rnd != null) {
            list.Swap(end, rnd.Next(start, end+1));
        }

        var pivot = list[end];
        int lastLow = start - 1;
        for (int i = start; i < end; i++) {
            if (list[i].CompareTo(pivot) <= 0) {
                list.Swap(i, ++lastLow);
            }
        }
        list.Swap(end, ++lastLow);
        return lastLow;
    }

    /// <summary>
    /// Returns Nth smallest element from the list. Here n starts from 0 so that n=0 returns minimum, n=1 returns 2nd smallest element etc.
    /// Note: specified list would be mutated in the process.
    /// Reference: Introduction to Algorithms 3rd Edition, Corman et al, pp 216
    /// </summary>
    public static T NthOrderStatistic<T>(this IList<T> list, int n, Random? rnd = null) where T : IComparable<T> {
        return NthOrderStatistic(list, n, 0, list.Count - 1, rnd);
    }
    private static T NthOrderStatistic<T>(this IList<T> list, int n, int start, int end, Random? rnd) where T : IComparable<T> {
        while (true) {
            int pivotIndex = list.Partition(start, end, rnd);
            if (pivotIndex == n) {
                return list[pivotIndex];
            }

            if (n < pivotIndex) {
                end = pivotIndex - 1;
            } else {
                start = pivotIndex + 1;
            }
        }
    }

    public static void Swap<T>(this IList<T> list, int i, int j) {
        // This check is not required but Partition function may make many calls so its for perf reason
        if (i == j) {
            return;
        }

        (list[i], list[j]) = (list[j], list[i]);
    }
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

    /// Adds an element to the list stored under the specified key
    public static void AddToKey<TKey, TValue>(this IDictionary<TKey, HashSet<TValue>> dict, TKey key, TValue value) {
        if (dict.TryGetValue(key, out var set)) {
            set.Add(value);
            return;
        }
        dict[key] = [value];
    }
    /// Adds all elements to the list stored under the specified key
    public static void AddRangeToKey<TKey, TValue>(this IDictionary<TKey, HashSet<TValue>> dict, TKey key, IEnumerable<TValue> values) {
        if (dict.TryGetValue(key, out var set)) {
            set.AddRange(values);
            return;
        }
        dict[key] = [..values];
    }
}

public static class HashSetExtensions {
    /// Adds a range of elements to the set
    public static void AddRange<T>(this HashSet<T> set, params IEnumerable<T> items) {
        switch (items) {
            case IList<T> list: {
#if NET7_0_OR_GREATER
                set.EnsureCapacity(set.Count + list.Count);
#endif
                for (int i = 0; i < list.Count; i++) {
                    set.Add(list[i]);
                }
                break;
            }
            case ICollection<T> collection: {
#if NET7_0_OR_GREATER
                set.EnsureCapacity(set.Count + collection.Count);
#endif
                foreach (var item in collection) {
                    set.Add(item);
                }
                break;
            }
            default: {
                foreach (var item in items) {
                    set.Add(item);
                }
                break;
            }
        }
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
