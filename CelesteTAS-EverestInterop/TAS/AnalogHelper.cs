using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS;

public enum AnalogueMode {
    Ignore,
    Circle,
    Square,
    Precise,
}

// ReSharper disable once StructCanBeMadeReadOnly
// mono explodes on loading the dll if there is a readonly struct in it on MacOS
public record struct Vector2Short(short X = 0, short Y = 0) {
    public readonly short X = X;
    public readonly short Y = Y;
}

public static class AnalogHelper {
    private const double DeadZone = 0.239532471;
    private const double DcMult = (1 - DeadZone) * (1 - DeadZone);
    private const float AmpLowerbound = (float) (0.25 * DcMult);
    private const short Lowerbound = 7849;

    private static readonly Regex Fractional = new(@"\d+\.(\d*)", RegexOptions.Compiled);
    private static AnalogueMode analogMode = AnalogueMode.Ignore;

    public static Vector2 ComputeAngleVector2(InputFrame input, out Vector2Short angleVector2Short) {
        float precision;
        if (input.Angle == 0) {
            precision = 1E-6f;
        } else {
            int digits = 0;
            Match match = Fractional.Match(input.Angle.ToString(CultureInfo.InvariantCulture));
            if (match.Success) {
                digits = match.Value.Length;
            }

            precision = float.Parse($"0.5E-{digits + 2}");
        }

        float x = input.GetX();
        float y = input.GetY();
        if (analogMode != AnalogueMode.Precise) {
            x *= input.UpperLimit;
            y *= input.UpperLimit;
        }

        Vector2 angleVector2 = ComputeFeather(x, y, precision, input.UpperLimit, out Vector2Short retDirectionShort);
        angleVector2Short = retDirectionShort;
        return angleVector2;
    }

    private static Vector2 ComputeFeather(float x, float y, float precision, float upperLimit, out Vector2Short retDirectionShort) {
        short RoundToValidShort(float f) {
            return (short) Math.Round((f * (1.0 - DeadZone) + DeadZone) * 32767);
        }

        if (x < 0) {
            Vector2 feather = ComputeFeather(-x, y, precision, upperLimit, out Vector2Short directionShort);
            retDirectionShort = new Vector2Short((short) -directionShort.X, directionShort.Y);
            return new Vector2(-feather.X, feather.Y);
        }

        if (y < 0) {
            Vector2 feather = ComputeFeather(x, -y, precision, upperLimit, out Vector2Short directionShort);
            retDirectionShort = new Vector2Short(directionShort.X, (short) -directionShort.Y);
            return new Vector2(feather.X, -feather.Y);
        }

        if (x < y) {
            Vector2 feather = ComputeFeather(y, x, precision, upperLimit, out Vector2Short directionShort);
            retDirectionShort = new Vector2Short(directionShort.Y, directionShort.X);
            return new Vector2(feather.Y, feather.X);
        }

        // assure positive and x>=y
        short shortX, shortY;
        switch (analogMode) {
            case AnalogueMode.Ignore:
            case AnalogueMode.Circle:
                shortX = RoundToValidShort(x);
                shortY = RoundToValidShort(y);
                break;
            case AnalogueMode.Square:
                float divisor = Math.Max(Math.Abs(x), Math.Abs(y));
                x /= divisor;
                y /= divisor;
                shortX = RoundToValidShort(x);
                shortY = RoundToValidShort(y);
                break;
            case AnalogueMode.Precise:
                short upperbound = (short) Math.Round(Calc.Clamp(upperLimit * 32767, Lowerbound, 32767), MidpointRounding.AwayFromZero);
                Vector2Short result = ComputePrecise(new Vector2(x, y), precision, upperbound);
                shortX = result.X;
                shortY = result.Y;
                break;
            default:
                throw new Exception("what the fuck");
        }

        retDirectionShort = new Vector2Short(shortX, shortY);

        if (analogMode == AnalogueMode.Ignore) {
            return new Vector2(x, y);
        }

        x = (float) (Math.Max(shortX / 32767.0 - DeadZone, 0.0) / (1 - DeadZone));
        y = (float) (Math.Max(shortY / 32767.0 - DeadZone, 0.0) / (1 - DeadZone));
        return new Vector2(x, y);
    }

    private static Vector2Short ComputePrecise(Vector2 direction, float precision, short upperbound) {
        // it should hold that direction has x>0, y>0, x>=y
        // we look for the least y/x difference
        if (direction.Y < 1e-10) {
            return new Vector2Short(upperbound, 0);
        }

        double approx = (double) direction.Y / (double) direction.X;
        double multip = (double) direction.X / (double) direction.Y;
        double upperl = (double) upperbound / 32767;
        double leastError = approx;
        short retX = upperbound, retY = 0; // y/x=0, so error is approx
        short y = Lowerbound;
        while (true) {
            double ys = (double) y / 32767 - DeadZone;
            double xx = Math.Min(DeadZone + multip * ys, upperl);
            short x = (short) Math.Floor(xx * 32767);
            double xs = (double) x / 32767 - DeadZone;
            double error = Math.Abs(ys / xs - approx);
            if (xs * xs + ys * ys >= AmpLowerbound && (error < leastError || error <= precision)) {
                leastError = error;
                retX = x;
                retY = y;
            }

            if (x < upperbound) {
                ++x;
                xs = (double) x / 32767 - DeadZone;
                error = Math.Abs(ys / xs - approx);
                if (xs * xs + ys * ys >= AmpLowerbound && (error < leastError || error <= precision)) {
                    leastError = error;
                    retX = x;
                    retY = y;
                }
            }

            if (xx >= upperl) {
                break;
            }

            ++y;
        }

        return new Vector2Short(retX, retY);
    }

    // AnalogMode, Mode
    // AnalogueMode, Mode
    [TasCommand("AnalogueMode", AliasNames = new[] {"AnalogMode"}, ExecuteTiming = ExecuteTiming.Parse)]
    private static void AnalogueModeCommand(string[] args, int _, string __, int line) {
        if (args.IsEmpty() || !Enum.TryParse(args[0], true, out AnalogueMode mode)) {
            AbortTas($"AnalogMode command failed at line {line}\nMode must be Ignore, Circle, Square or Precise");
        } else {
            analogMode = mode;
        }
    }

    [ClearInputs]
    private static void Reset() {
        analogMode = AnalogueMode.Ignore;
    }
}