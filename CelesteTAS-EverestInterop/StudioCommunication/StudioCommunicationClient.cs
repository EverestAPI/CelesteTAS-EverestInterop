using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
		}

		public static bool Run() {
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				return false;
			instance = new StudioCommunicationClient();

			ThreadStart mainLoop = new ThreadStart(instance.UpdateLoop);
			Thread updateThread = new Thread(mainLoop);
			updateThread.Start();
			return true;
		}

		protected override void WaitForConnection() {
			for (; ; ) {
				try {
					(pipe as NamedPipeClientStream).Connect(1000);
					ThreadStart establishConnection = new ThreadStart(EstablishConnection);
					new Thread(establishConnection).Start();
					break;
				}
				catch (TimeoutException) { }
			}
		}

		#region Read
		protected override void ReadData(IAsyncResult result) {
			if (readBuffer[0] == 0) {
				readBuffer = (byte[])result.AsyncState;
				if (readBuffer[0] == 0)
					return;
			}

			int size = LengthOfMessage(readBuffer);
			byte[] data = new byte[size];
			Buffer.BlockCopy(readBuffer, HEADER_LENGTH, data, 0, size);
			int fullSize = HEADER_LENGTH + size;
			Buffer.BlockCopy(readBuffer, fullSize, readBuffer, 0, readBuffer.Length - fullSize);

			switch (data[0]) {
				case (byte)MessageIDs.SendPath:
					ProcessSendPath(data);
					break;
				case (byte)MessageIDs.SendHotkeyPressed:
					ProcessHotkeyPressed(data);
					break;
				case (byte)MessageIDs.SendNewBindings:
					ProcessNewBindings(data);
					break;
				case (byte)MessageIDs.ReloadBindings:
					ProcessReloadBindings(data);
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

		protected override void EstablishConnection() {
			//Studio side
			//AddToBuffer(MessageIDs.EstablishConnection, new byte[0]);
			//WaitForConfirm(MessageIDs.EstablishConnection);

			//Celeste side
			Confirm(MessageIDs.EstablishConnection, true);

			//Studio side
			//SendPath(Studio.path);

			//Celeste side
			SendCurrentBindings(Hotkeys.listHotkeyKeys);
			Initialized = true;
		}

		public void SendState(string state) {
			byte[] stateBytes = Encoding.Default.GetBytes(state);
			AddToBuffer(MessageIDs.SendState, stateBytes);
		}

		public void SendPlayerData(string data) {
			byte[] dataBytes = Encoding.Default.GetBytes(data);
			AddToBuffer(MessageIDs.SendPlayerData, dataBytes);
		}

		public void SendCurrentBindings(List<Keys>[] bindings) {
			byte[] data = ToByteArray(bindings);
			AddToBuffer(MessageIDs.SendNewBindings, data);
			WaitForConfirm(MessageIDs.SendCurrentBindings);
		}
		#endregion

	}
}
