using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste;
using Microsoft.Xna.Framework.Input;
using TAS.EverestInterop;

namespace TAS.StudioCommunication {
	public sealed class StudioCommunicationClient : StudioCommunicationBase {

		public static StudioCommunicationClient instance;

		private StudioCommunicationClient() {
		}

		public static bool Run() {
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				return false;
			instance = new StudioCommunicationClient();

#if DEBUG
			SetupDebugVariables();
#endif

			//ThreadStart mainLoop = new ThreadStart(instance.UpdateLoop);
			//Thread updateThread = new Thread(mainLoop);
			//updateThread.Name = "StudioCom Client";
			//updateThread.Start();

			RunThread.Start((Action)instance.UpdateLoop, "StudioCom Client");

			return true;
		}


		private static void SetupDebugVariables() {
			Hotkeys.instance = new Hotkeys();
			Hotkeys.listHotkeyKeys = new List<Keys>[] {
				new List<Keys> { Keys.RightControl, Keys.OemOpenBrackets },
				new List<Keys> { Keys.RightControl, Keys.RightShift },
				new List<Keys> { Keys.OemOpenBrackets },
				new List<Keys> { Keys.OemCloseBrackets },
				new List<Keys> { Keys.V },
				new List<Keys> { Keys.B },
				new List<Keys> { Keys.N }
			};
		}

		#region Read


		protected override void ReadData(Message message) {
			switch (message.ID) {
				case MessageIDs.Wait:
					ProcessWait();
					break;
				case MessageIDs.SendPath:
					ProcessSendPath(message.Data);
					break;
				case MessageIDs.SendHotkeyPressed:
					ProcessHotkeyPressed(message.Data);
					break;
				case MessageIDs.SendNewBindings:
					ProcessNewBindings(message.Data);
					break;
				case MessageIDs.ReloadBindings:
					ProcessReloadBindings(message.Data);
					break;
				default:
					throw new InvalidOperationException($"{message.ID}");
			}
		}

		private void ProcessSendPath(byte[] data) {
			string path = Encoding.Default.GetString(data);
			Log(path);
			if (path != null)
				Manager.settings.DefaultPath = path;
		}

		private void ProcessHotkeyPressed(byte[] data) {
			HotkeyIDs hotkey = (HotkeyIDs)data[0];
			Log($"{hotkey.ToString()} pressed");

			if (hotkey == HotkeyIDs.FastForward)
				Hotkeys.hotkeys[data[0]].overridePressed = !Hotkeys.hotkeys[data[0]].overridePressed;
			else
				Hotkeys.hotkeys[data[0]].overridePressed = true;
		}

		private void ProcessNewBindings(byte[] data) {
			byte ID = data[0];
			List<Keys> keys = FromByteArray<List<Keys>>(data, 1);
			Log($"{((HotkeyIDs)ID).ToString()} set to {keys}");
			Hotkeys.listHotkeyKeys[ID] = keys;
		}

		private void ProcessReloadBindings(byte[] data) {
			Log("Reloading bindings");
			Hotkeys.instance.OnInputInitialize();
		}

		#endregion

		#region Write

		protected override void EstablishConnection() {
			var studio = this;
			var celeste = this;
			studio = null;

			Message? lastMessage;

			studio?.WriteMessageGuaranteed(new Message(MessageIDs.EstablishConnection, new byte[0]));
			celeste?.ReadMessageGuaranteed();

			celeste?.SendPath(Directory.GetCurrentDirectory());
			lastMessage = studio?.ReadMessageGuaranteed();
			studio?.ProcessSendPath(lastMessage?.Data);

			studio?.SendPath(null);
			lastMessage = celeste?.ReadMessageGuaranteed();
			celeste?.ProcessSendPath(lastMessage?.Data);

			celeste?.SendCurrentBindings(Hotkeys.listHotkeyKeys);
			lastMessage = studio?.ReadMessageGuaranteed();
			//studio?.ProcessSendCurrentBindings(lastMessage?.Data);

			Initialized = true;
		}

		private void SendPath(string path) {
			byte[] pathBytes = Encoding.Default.GetBytes(path);
			WriteMessageGuaranteed(new Message(MessageIDs.SendPath, pathBytes));
		}

		private void SendStateAndPlayerDataNow(string state, string playerData, bool canFail) {
			if (Initialized) {
				string[] data = new string[] { state, playerData };
				byte[] dataBytes = ToByteArray(data);
				Message message = new Message(MessageIDs.SendState, dataBytes);
				if (canFail)
					WriteMessage(message);
				else
					WriteMessageGuaranteed(message);
			}
		}

		public void SendStateAndPlayerData(string state, string playerData, bool canFail) {
			pendingWrite = () => SendStateAndPlayerDataNow(state, playerData, canFail);
		}

		private void SendCurrentBindings(List<Keys>[] bindings) {
			byte[] data = ToByteArray(bindings);
			WriteMessageGuaranteed(new Message(MessageIDs.SendCurrentBindings, data));
		}
#endregion

	}
}
