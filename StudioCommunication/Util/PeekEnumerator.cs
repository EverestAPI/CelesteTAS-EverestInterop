using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace StudioCommunication.Util;

/// Enumerator which allows peeking at the following iteration values,
/// without actually advancing the enumerator state
public class PeekEnumerator<T>(IEnumerable<T> enumerable) : IEnumerator, IDisposable {
    private readonly IEnumerator<T> enumerator = enumerable.GetEnumerator();
    private Queue<T>? queue;

    ~PeekEnumerator() => Dispose();

    public PeekEnumerator<T> GetEnumerator() => this;

    public T Current {
        get {
            if (queue == null || queue.Count == 0) {
                return enumerator.Current;
            }

            return queue.Peek();
        }
    }

    object? IEnumerator.Current => Current;

    public bool MoveNext() {
        if (queue != null && queue.Count > 0) {
            queue.Dequeue();
            return true;
        }

        return enumerator.MoveNext();
    }

    public void Reset() {
        enumerator.Reset();
        queue?.Clear();
    }

    public void Dispose() {
        enumerator.Dispose();
        GC.SuppressFinalize(this);
    }

    public T Peek(uint distance = 0) {
        queue ??= new();

        while (queue.Count <= distance) {
            if (!enumerator.MoveNext()) {
                throw new Exception("Out-of-bounds of enumeration");
            }

            queue.Enqueue(enumerator.Current);
        }

        return queue.Peek(distance);
    }
    public bool TryPeek([MaybeNullWhen(false)] out T value, uint distance = 0) {
        queue ??= new();

        if (queue.Count > distance) {
            value = queue.Peek(distance);
            return true;
        }

#if NET7_0_OR_GREATER
        queue.EnsureCapacity((int)(distance - queue.Count + 1));
#endif
        do {
            if (!enumerator.MoveNext()) {
                value = default;
                return false;
            }

            queue.Enqueue(value = enumerator.Current);
        } while (queue.Count <= distance);

        return true;
    }
}
