using System.Diagnostics.CodeAnalysis;

namespace TAS.Utils;

/// Represents a result value which might not be available, due to errors
public readonly record struct Result<TValue, TError>(TValue? Value, TError? Error) {
    public static Result<TValue, TError> Success(TValue value) => new(value, default);
    public static Result<TValue, TError> Fail(TError error) => new(default, error);

    public static implicit operator Result<TValue, TError>(TValue result) => new(result, default);
    public static implicit operator Result<TValue, TError>(TError error) => new(default, error);
}

/// Represents a void result value which might not be available, due to errors
public readonly record struct VoidResult<TError>(TError? Error) {
    public static readonly VoidResult<TError> Ok = new(default);
    public static VoidResult<TError> Fail(TError error) => new(error);

    public static implicit operator VoidResult<TError>(TError error) => new(error);

    public bool Success => Error == null;
    public bool Failure => Error != null;

    public static VoidResult<T> AggregateError<T>(VoidResult<T> lhs, VoidResult<T> rhs) {
        if (lhs.Error == null && rhs.Error == null) {
            return VoidResult<T>.Ok;
        }

        if (lhs.Error != null && rhs.Error == null) {
            return lhs;
        }
        if (lhs.Error == null && rhs.Error != null) {
            return rhs;
        }

        if (typeof(T) == typeof(string)) {
            return VoidResult<T>.Fail((T)(object) $"{lhs.Error}\n{rhs.Error}");
        }

        return lhs; // Drop rhs error since it can't be properly accumulated
    }
}
