using System;
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
