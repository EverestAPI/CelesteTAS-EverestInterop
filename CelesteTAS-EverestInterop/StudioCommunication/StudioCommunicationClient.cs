using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;
using TAS.EverestInterop;

namespace TAS.StudioCommunication {
	public sealed class StudioCommunicationClient : StudioCommunicationBase {

		public static StudioCommunicationClient instance;

		private StudioCommunicationClient() {
			pipe = new NamedPipeClientStream("CelesteTAS");
			//pipe.ReadMode = PipeTransmissionMode.Message;
			waitingForResponse = true;
			
		}

		public static bool Run() {
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				return false;
			instance = new StudioCommunicationClient();

#if DEBUG
			SetupDebugVariables();
#endif

			ThreadStart mainLoop = new ThreadStart(instance.UpdateLoop);
			Thread updateThread = new Thread(mainLoop);
			updateThread.Name = "StudioCom Client";
			updateThread.Start();
			return true;
		}



		protected override void WaitForConnection() {
			for (; ; ) {
				try {
					(pipe as NamedPipeClientStream).Connect(1000);
					ThreadStart establishConnection = new ThreadStart(EstablishConnection);
					Thread thread = new Thread(establishConnection);
					thread.Name = "Client Initialization";
					thread.Start();
					break;
				}
				catch (TimeoutException) { }
			}
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


		protected override void ReadSwitch(Message message) {
			switch (message.ID) {
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
					throw new InvalidOperationException();
			}
		}

		private void ProcessSendPath(byte[] data) {
			string path = Encoding.Default.GetString(data, 1, data.Length - 1);
			Manager.settings.DefaultPath = path;
			Confirm(MessageIDs.SendPath);
		}
		private void ProcessHotkeyPressed(byte[] data) {
			Hotkeys.hotkeys[data[1]].overridePressed = true;
		}
		private void ProcessNewBindings(byte[] data) {
			byte ID = data[1];
			List<Keys> keys = FromByteArray<List<Keys>>(data, 2);
			Hotkeys.listHotkeyKeys[ID] = keys;
			Confirm(MessageIDs.SendNewBindings);
		}
		private void ProcessReloadBindings(byte[] data) {
			Hotkeys.instance.OnInputInitialize();
			Confirm(MessageIDs.ReloadBindings);
		}

		#endregion

		#region Write

		protected override async void EstablishConnection() {
			//Studio side
			//WriteMessage(new Message(MessageIDs.EstablishConnection, new byte[0]));
			//WaitForConfirm(MessageIDs.EstablishConnection);

			//Celeste side
			await ReadMessage();
			await ReadMessage();
			Confirm(MessageIDs.EstablishConnection);
			ProcessSendPath(ReadMessage().Result.Data);

			//Studio side
			//SendPath(Studio.path);

			//Celeste side
			SendCurrentBindings(Hotkeys.listHotkeyKeys);

			Initialized = true;
		}

		public void SendState(string state) {
			byte[] stateBytes = Encoding.Default.GetBytes(state);
			WriteMessage(new Message(MessageIDs.SendState, stateBytes));
		}

		public void SendPlayerData(string data) {
			byte[] dataBytes = Encoding.Default.GetBytes(data);
			WriteMessage(new Message(MessageIDs.SendPlayerData, dataBytes));
		}

		public void SendCurrentBindings(List<Keys>[] bindings) {
			byte[] data = ToByteArray(bindings);
			WriteMessage(new Message(MessageIDs.SendCurrentBindings, data));
			WaitForConfirm(MessageIDs.SendCurrentBindings);
		}
#endregion

	}
}
