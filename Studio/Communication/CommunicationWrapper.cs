using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CelesteStudio.Properties;
using CelesteStudio.RichText;
using StudioCommunication;
using Char = CelesteStudio.RichText.Char;

namespace CelesteStudio.Communication {
    static class CommunicationWrapper {
        public static StudioInfo StudioInfo;
        public static string ReturnData;
        private static Dictionary<HotkeyID, List<Keys>> bindings;
        public static bool FastForwarding;

        [DllImport("User32.dll")]
        private static extern short GetAsyncKeyState(Keys key);

        private static bool IsKeyDown(Keys keys) {
            return (GetAsyncKeyState(keys) & 0x8000) == 0x8000;
        }

        public static void SetBindings(Dictionary<HotkeyID, List<Keys>> newBindings) {
            bindings = newBindings;
        }

        //"wrapper"
        //This doesn't work in release build and i don't particularly care to figure out why.
        public static bool CheckControls(ref Message msg) {
            if (!Settings.Default.UpdatingHotkeys
                || Environment.OSVersion.Platform == PlatformID.Unix
                || bindings == null
                // check if key is repeated
                || ((int) msg.LParam & 0x40000000) == 0x40000000) {
                return false;
            }

            bool anyPressed = false;
            foreach (HotkeyID hotkeyIDs in bindings.Keys) {
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
                    if (hotkeyIDs == HotkeyID.FastForward) {
                        FastForwarding = true;
                    }

                    StudioCommunicationServer.Instance?.SendHotkeyPressed(hotkeyIDs);
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
            if (bindings.ContainsKey(HotkeyID.FastForward)) {
                List<Keys> keys = bindings[HotkeyID.FastForward];
                pressed = keys.Count > 0 && keys.All(IsKeyDown);
            } else {
                pressed = false;
            }

            if (!pressed) {
                StudioCommunicationServer.Instance.SendHotkeyPressed(HotkeyID.FastForward, true);
                FastForwarding = false;
            }

            return pressed;
        }

        public static void UpdateLines(Dictionary<int, string> updateLines) {
            RichText.RichText tasText = Studio.Instance.richText;
            foreach (int lineNumber in updateLines.Keys) {
                string lineText = updateLines[lineNumber];
                if (tasText.Lines.Count > lineNumber) {
                    Line line = tasText.TextSource[lineNumber];
                    line.Clear();
                    if (lineText.Length > 0) {
                        line.AddRange(lineText.ToCharArray().Select(c => new Char(c)));
                        Range range = new(tasText, 0, lineNumber, line.Count, lineNumber);
                        range.SetStyle(SyntaxHighlighter.ChocolateStyle);
                    }
                }
            }

            if (updateLines.Count > 0) {
                tasText.NeedRecalc();
            }
        }
    }
}