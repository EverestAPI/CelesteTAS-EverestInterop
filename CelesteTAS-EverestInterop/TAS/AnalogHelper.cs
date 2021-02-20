using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TAS.Input;
using GameInput = Celeste.Input;

namespace TAS {
    public enum AnalogueMode {
        Ignore,
        Circle,
        Square,
        Precise,
    }

    public struct Vector2Short {
        public short X, Y;

        public Vector2Short(short x = 0, short y = 0) {
            X = x;
            Y = y;
        }
    }

    public static class AnalogHelper {
        private const double Deadzone = 0.239532471;
        private const double DcMult = (1 - Deadzone) * (1 - Deadzone);
        public const short Lowerbound = 7849;

        private static float ampLowerbound = (float) (0.25 * DcMult);
        private static AnalogueMode analogMode = AnalogueMode.Ignore;
        public static Vector2 LastDirection;
        public static Vector2Short LastDirectionShort;
        private static short upperbound = 32767;
        public static AnalogueMode Mode => analogMode;
        public static short Limit => upperbound;

        public static void AnalogModeChange(AnalogueMode mode, short newUpperbound = 32767, float newDeadzone = 0.5f) {
            if (mode != analogMode || upperbound != newUpperbound) {
                analogMode = mode;
                upperbound = newUpperbound;
                ampLowerbound = (float) (newDeadzone * newDeadzone * DcMult);
            }
        }

        private static Vector2Short ComputePrecise(Vector2 direction,float prec) {
            // it should hold that direction has x>0, y>0, x>y
            // we look for the least y/x difference
            if (direction.Y < 1e-10) {
                return new Vector2Short(upperbound, 0);
            }
            double approx = direction.Y / direction.X;
            double multip = direction.X / direction.Y;
            double upperl = (double) upperbound / 32767;
            double leastError = approx;
            short retX = upperbound, retY = 0; // y/x=0, so error is approx
            short y = Lowerbound;
            while (true) {
                double ys = (double) y / 32767 - Deadzone;
                double xx = Math.Min(Deadzone + multip * ys, upperl);
                short x = (short) Math.Floor(xx * 32767);
                double xs = (double) x / 32767 - Deadzone;
                double error = Math.Abs(ys / xs - approx);
                if (xs * xs + ys * ys >= ampLowerbound && (error < leastError || error <= prec)) {
                    leastError = error;
                    retX = x;
                    retY = y;
                }

                if (x < upperbound) {
                    ++x;
                    xs = (double) x / 32767 - Deadzone;
                    error = Math.Abs(ys / xs - approx);
                    if (xs * xs + ys * ys >= ampLowerbound && (error < leastError || error <= prec)) {
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

        private static Vector2 ComputeFeather(float x, float y, float prec) {
            if (x < 0) {
                Vector2 feather = ComputeFeather(-x, y, prec);
                LastDirectionShort.X = (short) -LastDirectionShort.X;
                return LastDirection = new Vector2(-feather.X, feather.Y);
            }

            if (y < 0) {
                Vector2 feather = ComputeFeather(x, -y, prec);
                LastDirectionShort.Y = (short) -LastDirectionShort.Y;
                return LastDirection = new Vector2(feather.X, -feather.Y);
            }

            if (x < y) {
                Vector2 feather = ComputeFeather(y, x, prec);
                LastDirectionShort = new Vector2Short(LastDirectionShort.Y, LastDirectionShort.X);
                return LastDirection = new Vector2(feather.Y, feather.X);
            }

            // assure positive and x>=y
            short shortX, shortY;
            switch (analogMode) {
                case AnalogueMode.Ignore:
                    return new Vector2(x, y);
                case AnalogueMode.Circle:
                    shortX = (short) Math.Round((x * (1.0 - Deadzone) + Deadzone) * 32767);
                    shortY = (short) Math.Round((y * (1.0 - Deadzone) + Deadzone) * 32767);
                    break;
                case AnalogueMode.Square:
                    float divisor = Math.Max(Math.Abs(x), Math.Abs(y));
                    x /= divisor;
                    y /= divisor;
                    shortX = (short) Math.Round((x * (1.0 - Deadzone) + Deadzone) * 32767);
                    shortY = (short) Math.Round((y * (1.0 - Deadzone) + Deadzone) * 32767);
                    break;
                case AnalogueMode.Precise:
                    Vector2Short result = ComputePrecise(new Vector2(x, y),prec);
                    shortX = result.X;
                    shortY = result.Y;
                    break;
                default:
                    throw new Exception("what the fuck");
            }

            LastDirectionShort = new Vector2Short(shortX, shortY);
            x = (float) (Math.Max(shortX / 32767.0 - Deadzone, 0.0) / (1 - Deadzone));
            y = (float) (Math.Max(shortY / 32767.0 - Deadzone, 0.0) / (1 - Deadzone));
            LastDirection = new Vector2(x, y);
            return LastDirection;
        }
        private static InputFrame LastQuery;
        static public Vector2 GetFeather(InputFrame input) {
            if (input == LastQuery)
                return LastDirection;
            LastQuery = input;
            return ComputeFeather(input.GetX(),input.GetY(),input.Precision);
        }
    }
}