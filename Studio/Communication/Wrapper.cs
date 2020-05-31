//using Xna = Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace CelesteStudio.Communication {

	static class Wrapper {
		public static string gamePath;
		public static string state;
		public static string playerData = "";
		public static List<Keys>[] bindings;

		public static bool updatingHotkeys = true;

		[DllImport("User32.dll")]
		public static extern short GetAsyncKeyState(Keys key);

		public static string LevelName() {
			int nameStart = playerData.IndexOf('[') + 1;
			int nameEnd = playerData.IndexOf(']');
			return playerData.Substring(nameStart, nameEnd);
		}

		public static void SetBindings(List<Keys>[] newBindings) {
			bindings = newBindings;
		}

		public static bool CheckControls() {
			if (!updatingHotkeys || Environment.OSVersion.Platform == PlatformID.Unix || bindings == null)
				return false;
			
			bool anyPressed = false;
			for (int i = 0; i < bindings.Length; i++) {

				if (i == (int)HotkeyIDs.FastForward)
					continue;
				
				List<Keys> keys = bindings[i];
				bool pressed = true;
				if (keys == null || keys.Count == 0)
					pressed = false;
				foreach (Keys key in keys) {
					if ((GetAsyncKeyState(key) & 0x8000) != 0x8000) {
						pressed = false;
						break;
					}
				}
				if (pressed) {
					StudioCommunicationServer.instance.SendHotkeyPressed((HotkeyIDs)i);
					anyPressed = true;
				}
			}
			return anyPressed;
		}

		public static void CheckFastForward() {
			if (Environment.OSVersion.Platform == PlatformID.Unix || bindings == null)
				return;

			List<Keys> keys = bindings[(int)HotkeyIDs.FastForward];
			bool pressed = true;
			if (keys == null || keys.Count == 0)
				pressed = false;
			foreach (Keys key in keys) {
				if ((GetAsyncKeyState(key) & 0x8000) != 0x8000) {
					pressed = false;
					break;
				}
			}
			if (!pressed) {
				StudioCommunicationServer.instance.SendHotkeyPressed(HotkeyIDs.FastForward);
			}
		}
	}
}
