using Celeste;
using System;

namespace TAS.Utils;

/// Holds a position with integer and fractional part separated
internal record struct SubpixelPosition(SubpixelComponent X, SubpixelComponent Y) {
    public Vector2 Exact => new(X.Exact, Y.Exact);

    public static SubpixelPosition FromActor(Actor actor) {
        return new SubpixelPosition(
            new SubpixelComponent((int) actor.Position.X, actor.movementCounter.X),
            new SubpixelComponent((int) actor.Position.Y, actor.movementCounter.Y));
    }
    public static SubpixelPosition FromPlatform(Platform platform) {
        return new SubpixelPosition(
            new SubpixelComponent((int) platform.Position.X, platform.movementCounter.X),
            new SubpixelComponent((int) platform.Position.Y, platform.movementCounter.Y));
    }

    public string FormatValue(int decimals, bool subpixelRounding) {
        return $"{X.FormatValue(decimals, subpixelRounding)}, {Y.FormatValue(decimals, subpixelRounding)}";
    }

    public static SubpixelPosition operator +(SubpixelPosition lhs, SubpixelPosition rhs) => new(lhs.X + rhs.X, lhs.Y + rhs.Y);
    public static SubpixelPosition operator -(SubpixelPosition lhs, SubpixelPosition rhs) => new(lhs.X - rhs.X, lhs.Y - rhs.Y);
    public static SubpixelPosition operator *(SubpixelPosition lhs, float scalar) => new(lhs.X * scalar, lhs.Y * scalar);
    public static SubpixelPosition operator /(SubpixelPosition lhs, float scalar) => new(lhs.X / scalar, lhs.Y / scalar);
}

/// Holds a single axis with integer and fractional part separated
internal record struct SubpixelComponent(int Position, float Remainder) {
    public float Exact => Position + Remainder;

    public string FormatValue(int decimals, bool subpixelRounding) {
#if false
        if (decimals == 0 || !subpixelRounding) {
            return Exact.ToFormattedString(decimals);
        }

        double round = Math.Round(Exact, decimals);

        switch (Math.Abs(Remainder)) {
            case 0.5f:
                // Don't show subsequent zeros when subpixel is exactly equal to 0.5
                return round.ToString("F1");

            case < 0.5f:
                // Make 0.495 round away from 0.50
                int diffX = Position - (int) Math.Round(round, MidpointRounding.AwayFromZero);
                if (diffX != 0) {
                    round += diffX * Math.Pow(10, -decimals);
                }

                break;
        }

        return round.ToFormattedString(decimals);
#else
        return Exact.ToFormattedString(decimals);
#endif
    }

    public static SubpixelComponent operator +(SubpixelComponent lhs, SubpixelComponent rhs) {
        float rem = lhs.Remainder + rhs.Remainder;
        int round = (int)Math.Round(rem, MidpointRounding.ToEven);

        return new SubpixelComponent(lhs.Position + rhs.Position + round, rem - round);
    }
    public static SubpixelComponent operator -(SubpixelComponent lhs, SubpixelComponent rhs) {
        float rem = lhs.Remainder - rhs.Remainder;
        int round = (int)Math.Round(rem, MidpointRounding.ToEven);

        return new SubpixelComponent(lhs.Position - rhs.Position + round, rem - round);
    }

    public static SubpixelComponent operator *(SubpixelComponent lhs, float scalar) {
        float pos = lhs.Exact * scalar;
        float rem = pos - (int)pos;
        int round = (int)Math.Round(rem, MidpointRounding.ToEven);

        return new SubpixelComponent((int)pos + round, rem - round);
    }
    public static SubpixelComponent operator /(SubpixelComponent lhs, float scalar) {
        float pos = lhs.Exact / scalar;
        float rem = pos - (int)pos;
        int round = (int)Math.Round(rem, MidpointRounding.ToEven);

        return new SubpixelComponent((int)pos + round, rem - round);
    }
}
