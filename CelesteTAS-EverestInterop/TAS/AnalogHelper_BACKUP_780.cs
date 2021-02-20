using System;
using System.Collections.Generic;
<<<<<<< HEAD
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Celeste;
using GameInput = Celeste.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using TAS.Input;
using Microsoft.Xna.Framework.Graphics;
using FMOD;

namespace TAS {
    using AnalogueMode = Manager.AnalogueMode;
    public class VS2{
        public short x, y;
        public VS2(short xx=0,short yy = 0){
            x = xx;
            y = yy;
        }
    };
    
    public class AnalogHelper {
        public Vector2 LastDirection;
        public VS2 LastDS;
        private Dictionary<Vector2, VS2> ConvertCache;
        private AnalogueMode AnalogMode;
        public AnalogueMode mode { get { return AnalogMode;  } }
        private short Upperbound;
        public short limit { get { return Upperbound;  } }
        private short Lowerbound;
        private float AmpLowerbound;
        public float dead { get { return AmpLowerbound; } }
        private const double deadzone = 0.239532471;
        private const double dcMult = (1 - deadzone) * (1 - deadzone);
        public AnalogHelper() {
            LastDirection = new Vector2();
            LastDS = new VS2();
            ConvertCache = new Dictionary<Vector2, VS2>();
            Upperbound = 32767;
            Lowerbound = 7849;
            AmpLowerbound = (float)(0.25*dcMult);
            AnalogMode = AnalogueMode.Ignore;
        }
        public void AnalogModeChange(AnalogueMode mode, short NewUpperbound = 32767,float NewDeadzone=0.5f) {
            if(mode!=AnalogMode || Upperbound != NewUpperbound) {
                AnalogMode = mode;
                Upperbound = NewUpperbound;
                AmpLowerbound = (float)(NewDeadzone * NewDeadzone*dcMult);
                ConvertCache.Clear();
            }
        }
        private VS2 ComputePrecise(Vector2 direction) {
            // it should hold that direction has x>0, y>0, x>y
            // we look for the least y/x difference
            if (direction.Y < 1e-10)
                return new VS2(Upperbound, 0);
=======
using Microsoft.Xna.Framework;
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

        private static readonly Dictionary<Vector2, Vector2Short> ConvertCache = new Dictionary<Vector2, Vector2Short>();
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
                ConvertCache.Clear();
            }
        }

        private static Vector2Short ComputePrecise(Vector2 direction) {
            // it should hold that direction has x>0, y>0, x>y
            // we look for the least y/x difference
            if (direction.Y < 1e-10) {
                return new Vector2Short(upperbound, 0);
            }

>>>>>>> a96b76af68dde8ac6a4f8947c72c02a0fe8e7020
            if (ConvertCache.ContainsKey(direction)) {
                return ConvertCache[direction];
            } else {
                double approx = direction.Y / direction.X;
                double multip = direction.X / direction.Y;
<<<<<<< HEAD
                double upperl = (double)Upperbound / 32767;
                double leastError = approx;
                short RetX = Upperbound, RetY = 0; // y/x=0, so error is approx
                short Y = Lowerbound;
                while (true) {
                    double Ys = (double)Y / 32767 - deadzone;
                    double Xx = Math.Min(deadzone + multip * Ys,upperl);
                    short X = (short)Math.Floor(Xx * 32767);
                    double Xs = (double)X / 32767 - deadzone;
                    double error = Math.Abs(Ys / Xs - approx);
                    if(Xs*Xs + Ys*Ys >= AmpLowerbound && error < leastError) {
                        leastError = error;
                        RetX = X;
                        RetY = Y;
                    }
                    if (X < Upperbound) {
                        ++X;
                        Xs = (double)X / 32767 - deadzone;
                        error = Math.Abs(Ys / Xs - approx);
                        if (Xs * Xs + Ys * Ys >= AmpLowerbound && error < leastError) {
                            leastError = error;
                            RetX = X;
                            RetY = Y;
                        }
                    }
                    if (Xx >= upperl)
                        break;
                    ++Y;
                }
                return ConvertCache[direction]=new VS2(RetX, RetY);
            }
        }
        public Vector2 ComputeFeather(float x, float y) {
            if (x < 0) {
                Vector2 feather = ComputeFeather(-x, y);
                LastDS.x = (short)-LastDS.x;
                return LastDirection = new Vector2(-feather.X, feather.Y);
            }
            if (y < 0) {
                Vector2 feather = ComputeFeather(x, -y);
                LastDS.y = (short)-LastDS.y;
                return LastDirection = new Vector2(feather.X, -feather.Y);
            }
            if (x < y) {
                Vector2 feather = ComputeFeather(y, x);
                LastDS = new VS2(LastDS.y,LastDS.x);
                return LastDirection = new Vector2(feather.Y, feather.X);
            }
            /// assure positive and x>y
            short X, Y;
            switch (AnalogMode) {
                case AnalogueMode.Ignore:
                    return new Vector2(x, y);
                case AnalogueMode.Circle:
                    X = (short)Math.Round((x * (1.0 - deadzone) + deadzone) * 32767);
                    Y = (short)Math.Round((y * (1.0 - deadzone) + deadzone) * 32767);
=======
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
                    if (xs * xs + ys * ys >= ampLowerbound && error < leastError) {
                        leastError = error;
                        retX = x;
                        retY = y;
                    }

                    if (x < upperbound) {
                        ++x;
                        xs = (double) x / 32767 - Deadzone;
                        error = Math.Abs(ys / xs - approx);
                        if (xs * xs + ys * ys >= ampLowerbound && error < leastError) {
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

                return ConvertCache[direction] = new Vector2Short(retX, retY);
            }
        }

        public static Vector2 ComputeFeather(float x, float y) {
            if (x < 0) {
                Vector2 feather = ComputeFeather(-x, y);
                LastDirectionShort.X = (short) -LastDirectionShort.X;
                return LastDirection = new Vector2(-feather.X, feather.Y);
            }

            if (y < 0) {
                Vector2 feather = ComputeFeather(x, -y);
                LastDirectionShort.Y = (short) -LastDirectionShort.Y;
                return LastDirection = new Vector2(feather.X, -feather.Y);
            }

            if (x < y) {
                Vector2 feather = ComputeFeather(y, x);
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
>>>>>>> a96b76af68dde8ac6a4f8947c72c02a0fe8e7020
                    break;
                case AnalogueMode.Square:
                    float divisor = Math.Max(Math.Abs(x), Math.Abs(y));
                    x /= divisor;
                    y /= divisor;
<<<<<<< HEAD
                    X = (short)Math.Round((x * (1.0 - deadzone) + deadzone) * 32767);
                    Y = (short)Math.Round((y * (1.0 - deadzone) + deadzone) * 32767);
                    break;
                case AnalogueMode.Precise:
                    VS2 Result = ComputePrecise(new Vector2(x, y));
                    X = Result.x;
                    Y = Result.y;
=======
                    shortX = (short) Math.Round((x * (1.0 - Deadzone) + Deadzone) * 32767);
                    shortY = (short) Math.Round((y * (1.0 - Deadzone) + Deadzone) * 32767);
                    break;
                case AnalogueMode.Precise:
                    Vector2Short result = ComputePrecise(new Vector2(x, y));
                    shortX = result.X;
                    shortY = result.Y;
>>>>>>> a96b76af68dde8ac6a4f8947c72c02a0fe8e7020
                    break;
                default:
                    throw new Exception("what the fuck");
            }
<<<<<<< HEAD
            LastDS = new VS2(X, Y);
            x = (float)((Math.Max((float)X / 32767.0 - deadzone, 0.0)) / (1 - deadzone));
            y = (float)((Math.Max((float)Y / 32767.0 - deadzone, 0.0)) / (1 - deadzone));
=======

            LastDirectionShort = new Vector2Short(shortX, shortY);
            x = (float) (Math.Max(shortX / 32767.0 - Deadzone, 0.0) / (1 - Deadzone));
            y = (float) (Math.Max(shortY / 32767.0 - Deadzone, 0.0) / (1 - Deadzone));
>>>>>>> a96b76af68dde8ac6a4f8947c72c02a0fe8e7020
            LastDirection = new Vector2(x, y);
            return LastDirection;
        }
    }
<<<<<<< HEAD
}
=======
}
>>>>>>> a96b76af68dde8ac6a4f8947c72c02a0fe8e7020
