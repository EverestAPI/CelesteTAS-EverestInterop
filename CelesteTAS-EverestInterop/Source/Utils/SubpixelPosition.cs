namespace TAS.Utils;

/// Holds a position with integer and fractional part separated
internal record struct SubpixelPosition(SubpixelComponent X, SubpixelComponent Y);

/// Holds a single axis with integer and fractional part separated
internal record struct SubpixelComponent(int Position, float Remainder);
