namespace TAS.Utils;

/// Box for using value-types in a reference-type context
public class Box<T>(T value) where T : struct {
    public T Value = value;
}

/// Box for using value-types in a reference-type context
public class ReadonlyBox<T>(T value) where T : struct {
    public readonly T Value = value;
}
