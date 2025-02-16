using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TAS.Utils;

/// Lazily computes the containing value, only once required
internal struct LazyValue<T> {
    private T value;
    private bool initialized = false;

    private readonly Func<T> initializer;

    public LazyValue(Func<T> initializer) {
        Unsafe.SkipInit(out value);
        this.initializer = initializer;
    }

    /// Queries the containing value, computing it if required
    public T Value {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            if (initialized) {
                return value;
            }

            value = initializer();
            initialized = true;
            return value;
        }
    }

    /// Drops the cached value and recomputes it once accessed
    public void Reset() => initialized = false;
}

/// Lazily computes the content of the HashSet, only once required
internal struct LazySet<T>(Action<HashSet<T>> populator) {
    private readonly HashSet<T> value = [];
    private bool populated = false;

    /// Queries the containing value, computing it if required
    public HashSet<T> Value {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            if (populated) {
                return value;
            }

            value.Clear();
            populator(value);
            return value;
        }
    }

    /// Drops the cached content and recomputes it once accessed
    public void Reset() => populated = false;
}
