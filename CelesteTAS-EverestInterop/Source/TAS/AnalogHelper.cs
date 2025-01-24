using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using System.Collections.Generic;
using TAS.Input;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS;

public readonly record struct Vector2Short(short X, short Y);

public enum AnalogMode {
    /// Simply maps the angle onto a circle and applies the magnitude
    Ignore,
    /// Maps the angle onto a circle and applies the magnitude, but applies a deadzone of 0.24
    Circle,
    /// Maps the angle onto a square, but applies a deadzone of 0.24
    Square,
    /// Adjusts magnitude to find an exact angle, axis are individually limited to an upper bound
    Precise,
}

/// Game controllers only support a precision of a short for the X / Y axis
/// Depending on the AnalogMode, angle + magnitude are mapped to a valid short position
public static class AnalogHelper {
    private const double DeadZone = 0.239532471;
    private const double DcMult = (1 - DeadZone) * (1 - DeadZone);
    private const float AmpLowerbound = (float) (0.25 * DcMult);
    private const short Lowerbound = 7849;

    private static readonly Regex Fractional = new(@"\d+\.(\d*)", RegexOptions.Compiled);
    private static AnalogMode analogMode = AnalogMode.Ignore;

    public static (Vector2, Vector2Short) ComputeAngleVector(float angle, float magnitude) {
        float precision;
        if (angle == 0.0f) {
            precision = 1E-6f;
        } else {
            int digits = 0;

            var match = Fractional.Match(angle.ToString(CultureInfo.InvariantCulture));
            if (match.Success) {
                digits = match.Value.Length;
            }

            precision = float.Parse($"0.5E-{digits + 2}");
        }

        // Exactly map cardinal directions to avoid precision loss
        float x = angle switch {
            0.0f => 0.0f,
            90.0f => 1.0f,
            180.0f => 0.0f,
            270.0f => -1.0f,
            360.0f => 0.0f,
            _ => (float) Math.Sin(angle / 180.0 * Math.PI)
        };
        float y = angle switch {
            0.0f => 1.0f,
            90.0f => 0.0f,
            180.0f => -1.0f,
            270.0f => 0.0f,
            360.0f => 1.0f,
            _ => (float) Math.Cos(angle / 180.0 * Math.PI)
        };
        if (analogMode != AnalogMode.Precise) {
            x *= magnitude;
            y *= magnitude;
        }

        return ComputeFeather(x, y, precision, magnitude);
    }

    private static (Vector2, Vector2Short) ComputeFeather(float x, float y, float precision, float upperLimit) {
        static short RoundToValidShort(float f) {
            return (short) Math.Round((f * (1.0 - DeadZone) + DeadZone) * 32767);
        }

        // Assure both are positive and x >= y
        if (x < 0) {
            var (direction, directionShort) = ComputeFeather(-x, y, precision, upperLimit);
            return (new Vector2(-direction.X, direction.Y), new Vector2Short((short) -directionShort.X, directionShort.Y));
        }
        if (y < 0) {
            var (direction, directionShort) = ComputeFeather(x, -y, precision, upperLimit);
            return (new Vector2(direction.X, -direction.Y), new Vector2Short(directionShort.X, (short) -directionShort.Y));
        }
        if (x < y) {
            var (direction, directionShort) = ComputeFeather(y, x, precision, upperLimit);
            return (new Vector2(direction.Y, direction.X), new Vector2Short(directionShort.Y, directionShort.X));
        }

        Debug.Assert(x >= 0.0f);
        Debug.Assert(y >= 0.0f);
        Debug.Assert(x >= y);

        short shortX, shortY;
        switch (analogMode) {
            case AnalogMode.Ignore:
            case AnalogMode.Circle:
                shortX = RoundToValidShort(x);
                shortY = RoundToValidShort(y);
                break;

            case AnalogMode.Square:
                float divisor = Math.Max(Math.Abs(x), Math.Abs(y));
                x /= divisor;
                y /= divisor;
                shortX = RoundToValidShort(x);
                shortY = RoundToValidShort(y);
                break;

            case AnalogMode.Precise:
                short upperbound = (short) Math.Round(Calc.Clamp(upperLimit * 32767, Lowerbound, 32767), MidpointRounding.AwayFromZero);
                Vector2Short result = ComputePrecise(new Vector2(x, y), precision, upperbound);
                shortX = result.X;
                shortY = result.Y;
                break;

            default:
                throw new UnreachableException();
        }

        if (analogMode == AnalogMode.Ignore) {
            return (new Vector2(x, y), new Vector2Short(shortX, shortY));
        }

        x = (float) (Math.Max(shortX / 32767.0 - DeadZone, 0.0) / (1 - DeadZone));
        y = (float) (Math.Max(shortY / 32767.0 - DeadZone, 0.0) / (1 - DeadZone));
        return (new Vector2(x, y), new Vector2Short(shortX, shortY));
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

    private class Meta : ITasCommandMeta {
        public string Insert => $"AnalogMode{CommandInfo.Separator}[0;Ignore/Circle/Square/Precise]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length != 1) {
                yield break;
            }

            foreach (var mode in Enum.GetValues<AnalogMode>()) {
                yield return new CommandAutoCompleteEntry { Name = mode.ToString(), IsDone = true };
            }
        }
    }

    // AnalogMode, Mode
    [TasCommand("AnalogMode", Aliases = ["AnalogueMode"], ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(Meta))]
    private static void AnalogModeCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.IsEmpty() || !Enum.TryParse(args[0], true, out AnalogMode mode)) {
            AbortTas($"AnalogMode command failed at line {fileLine}\nMode must be Ignore, Circle, Square or Precise");
        } else {
            analogMode = mode;
        }
    }

    [ClearInputs]
    private static void Reset() {
        analogMode = AnalogMode.Ignore;
    }
}
