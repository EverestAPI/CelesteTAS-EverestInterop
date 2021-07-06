using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CelesteStudio.Properties;
using StudioCommunication;

namespace CelesteStudio.Communication {
    static class CommunicationWrapper {
        public static string gamePath;
        public static string state;
        public static string gameData = "";
        public static string command;
        private static Dictionary<HotkeyIDs, List<Keys>> bindings;

        public static bool updatingHotkeys = Settings.Default.UpdatingHotkeys;
        public static bool fastForwarding = false;

        private static readonly Regex LevelAndTimerRegex =
            new(@"^\[([^\[]+?)\] Timer: ([0-9.]+?\(\d+\))$", RegexOptions.Compiled | RegexOptions.Multiline);

        [DllImport("User32.dll")]
        private static extern short GetAsyncKeyState(Keys key);

        private static bool IsKeyDown(Keys keys) {
            return (GetAsyncKeyState(keys) & 0x8000) == 0x8000;
        }

        public static string LevelName() {
            if (LevelAndTimerRegex.IsMatch(gameData)) {
                return LevelAndTimerRegex.Match(gameData).Groups[1].Value;
            } else {
                return string.Empty;
            }
        }

        public static string Timer() {
            if (LevelAndTimerRegex.IsMatch(gameData)) {
                return LevelAndTimerRegex.Match(gameData).Groups[2].Value;
            } else {
                return string.Empty;
            }
        }

        public static void SetBindings(Dictionary<HotkeyIDs, List<Keys>> newBindings) {
            bindings = newBindings;
        }

        //"wrapper"
        //This doesn't work in release build and i don't particularly care to figure out why.
        public static bool CheckControls(ref Message msg) {
            if (!updatingHotkeys
                || Environment.OSVersion.Platform == PlatformID.Unix
                || bindings == null
                // check if key is repeated
                || ((int) msg.LParam & 0x40000000) == 0x40000000) {
                return false;
            }

            bool anyPressed = false;
            foreach (HotkeyIDs hotkeyIDs in bindings.Keys) {
                List<Keys> keys = bindings[hotkeyIDs];

                bool pressed = keys.Count > 0 && keys.All(IsKeyDown);

                if (pressed && keys.Count == 1) {
                    if (!keys.Contains(Keys.LShiftKey) && IsKeyDown(Keys.LShiftKey)) {
                        pressed = false;
                    }

                    if (!keys.Contains(Keys.RShiftKey) && IsKeyDown(Keys.RShiftKey)) {
                        pressed = false;
                    }

                    if (!keys.Contains(Keys.LControlKey) && IsKeyDown(Keys.LControlKey)) {
                        pressed = false;
                    }

                    if (!keys.Contains(Keys.RControlKey) && IsKeyDown(Keys.RControlKey)) {
                        pressed = false;
                    }
                }

                if (pressed) {
                    if (hotkeyIDs == HotkeyIDs.FastForward) {
                        fastForwarding = true;
                    }

                    StudioCommunicationServer.instance?.SendHotkeyPressed(hotkeyIDs);
                    anyPressed = true;
                }
            }

            return anyPressed;
        }

        public static bool CheckFastForward() {
            if (Environment.OSVersion.Platform == PlatformID.Unix || bindings == null) {
                throw new InvalidOperationException();
            }

            bool pressed;
            if (bindings.ContainsKey(HotkeyIDs.FastForward)) {
                List<Keys> keys = bindings[HotkeyIDs.FastForward];
                pressed = keys.Count > 0 && keys.All(IsKeyDown);
            } else {
                pressed = false;
            }

            if (!pressed) {
                StudioCommunicationServer.instance.SendHotkeyPressed(HotkeyIDs.FastForward, true);
                fastForwarding = false;
            }

            return pressed;
        }
    }
}