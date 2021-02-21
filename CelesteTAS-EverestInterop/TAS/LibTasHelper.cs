using System.IO;
using System.Text;
using TAS.Input;

namespace TAS {
    public static class LibTasHelper {
        private static StreamWriter streamWriter;
        private static InputFrame skipInputFrame;
        private static string path;

        public static bool exportLibTas;

        public static void StartExport(string path) {
            FinishExport();
            streamWriter = new StreamWriter(path, false, Encoding.ASCII, 1 << 20);
            LibTasHelper.path = path;
            skipInputFrame = null;
            exportLibTas = true;
        }

        public static void RestartExport() {
            StartExport(path);
        }

        public static void FinishExport() {
            streamWriter?.Flush();
            streamWriter?.Dispose();
            streamWriter = null;
            path = "";
            skipInputFrame = null;
            exportLibTas = false;
        }

        public static void WriteLibTasFrame(InputFrame inputFrame) {
            if (!exportLibTas || inputFrame == skipInputFrame) {
                return;
            }

            for (int i = 0; i < inputFrame.Frames; ++i) {
                WriteLibTasFrame(LibTasKeys(inputFrame),
                    inputFrame.HasActions(Actions.Feather) ? ($"{inputFrame.AngleVector2Short.X}:{-inputFrame.AngleVector2Short.Y}") : "0:0",
                    LibTasButtons(inputFrame));
            }
        }

        public static void AddInputFrame(string inputText) {
            if (!exportLibTas) {
                return;
            }

            if (InputFrame.TryParse(inputText, 0, out InputFrame inputFrame)) {
                WriteLibTasFrame(inputFrame);
            }
        }

        public static void SkipNextInput() {
            if (exportLibTas) {
                skipInputFrame = Manager.Controller.Current;
            }
        }

        public static void ConvertToLibTas(string fileName) {
            if (string.IsNullOrEmpty(fileName)) {
                fileName = "libTAS_inputs.txt";
            }

            Manager.DisableExternal();
            StartExport(fileName);
            Manager.Controller.RefreshInputs(true);
            Manager.DisableExternal();
        }

        private static void WriteLibTasFrame(string outputKeys, string outputAxes, string outputButtons) {
            streamWriter.WriteLine($"|{outputKeys}|{outputAxes}:0:0:0:0:{outputButtons}|.........|");
        }

        private static string LibTasKeys(InputFrame inputFrame) {
            if (inputFrame.HasActions(Actions.Confirm)) {
                return "ff0d";
            }

            if (inputFrame.HasActions(Actions.Restart)) {
                return "72";
            }

            if (inputFrame.HasActions(Actions.Journal)) {
                return "ff09";
            }

            return "";
        }

        private static string LibTasButtons(InputFrame inputFrame) {
            // 0 BUTTON_A = A
            // 1 BUTTON_B = B
            // 2 BUTTON_X = X
            // 3 BUTTON_Y = Y
            // 4 BUTTON_BACK = b
            // 5 BUTTON_GUIDE = g
            // 6 BUTTON_START = s
            // 7 BUTTON_LEFTSTICK = (
            // 8 BUTTON_RIGHTSTICK = )
            // 9 BUTTON_LEFTSHOULDER = [
            // 10 BUTTON_RIGHTSHOULDER = ]
            // 11 BUTTON_DPAD_UP = u
            // 12 BUTTON_DPAD_DOWN = d
            // 13 BUTTON_DPAD_LEFT = l
            // 14 BUTTON_DPAD_RIGHT = r

            char[] buttons = new char[15];
            for (int i = 0; i < 15; ++i) {
                buttons[i] = '.';
            }

            if (inputFrame.HasActions(Actions.Left)) {
                buttons[13] = 'l';
            }

            if (inputFrame.HasActions(Actions.Right)) {
                buttons[14] = 'r';
            }

            if (inputFrame.HasActions(Actions.Up)) {
                buttons[11] = 'u';
            }

            if (inputFrame.HasActions(Actions.Down)) {
                buttons[12] = 'd';
            }

            if (inputFrame.HasActions(Actions.Jump)) {
                buttons[0] = 'A';
            }

            if (inputFrame.HasActions(Actions.Jump2)) {
                buttons[3] = 'Y';
            }

            if (inputFrame.HasActions(Actions.DemoDash)) {
                // Playback via libtas requires the right shoulder to be set to demodash
                buttons[10] = ']';
            }

            if (inputFrame.HasActions(Actions.Dash)) {
                buttons[1] = 'B';
            }

            if (inputFrame.HasActions(Actions.Dash2)) {
                buttons[2] = 'X';
            }

            if (inputFrame.HasActions(Actions.Start)) {
                buttons[6] = 's';
            }

            if (inputFrame.HasActions(Actions.Grab)) {
                buttons[9] = '[';
            }

            return string.Join("", buttons);
        }
    }
}