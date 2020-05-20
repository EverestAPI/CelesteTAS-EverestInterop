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

using MessageIDs = TAS.StudioCommunication.MessageIDs;
using HotkeyIDs = TAS.StudioCommunication.HotkeyIDs;
using StudioCommunicationBase = TAS.StudioCommunication.StudioCommunicationBase;

namespace TASALT.StudioCommunication {
	public sealed class StudioCommunicationServer : StudioCommunicationBase {

		private class FakeStudio {
			public string state;
			public string playerData;
			public List<Keys>[] bindings;
			public string path = Directory.GetCurrentDirectory() + "/Celeste.tas";
		}


		private FakeStudio Studio = new FakeStudio();
		public static StudioCommunicationServer instance;

		private StudioCommunicationServer() {
			pipe = new NamedPipeServerStream("CelesteTAS");
		}

		public static void Run() {
			instance = new StudioCommunicationServer();

			ThreadStart mainLoop = new ThreadStart(instance.UpdateLoop);
			Thread updateThread = new Thread(mainLoop);
			updateThread.Start();
		}

		#region Read
		protected override void ReadData(IAsyncResult result) {
			if (result != null) {
				readBuffer = (byte[])result.AsyncState;
				if (readBuffer[0] == 0)
					return;
			}
			int size = LengthOfMessage(readBuffer);
			if (size > 0) {

				byte[] data = new byte[size];
				Buffer.BlockCopy(readBuffer, HEADER_LENGTH, data, 0, size);
				int fullSize = HEADER_LENGTH + size;
				Buffer.BlockCopy(readBuffer, fullSize, readBuffer, 0, readBuffer.Length - fullSize);

				switch (data[0]) {
					case (byte)MessageIDs.SendState:
						ProcessSendState(data);
						break;
					case (byte)MessageIDs.SendPlayerData:
						ProcessSendPlayerData(data);
						break;
					case (byte)MessageIDs.SendCurrentBindings:
						ProcessSendCurrentBindings(data);
						break;
					default:
						throw new InvalidOperationException();
				}
			}
		}

		protected override void WaitForConnection() {
			(pipe as NamedPipeServerStream).WaitForConnection();
			ThreadStart establishConnection = new ThreadStart(EstablishConnection);
			new Thread(establishConnection).Start();
		}

		private void ProcessSendState(byte[] data) {
			string state = Encoding.Default.GetString(data, 1, data.Length - 1);
			Studio.state = state;
		}

		private void ProcessSendPlayerData(byte[] data) {
			string playerData = Encoding.Default.GetString(data, 1, data.Length - 1);
			Studio.playerData = playerData;
		}

		private void ProcessSendCurrentBindings(byte[] data) {
			List<Keys>[] keys = FromByteArray<List<Keys>[]>(data, 2);
			Studio.bindings = keys;
			Confirm(MessageIDs.SendCurrentBindings);
		}

		#endregion

		#region Write


		protected override void EstablishConnection() {
			//Studio side
			AddToBuffer(MessageIDs.EstablishConnection, new byte[0]);
			WaitForConfirm(MessageIDs.EstablishConnection);

			//Celeste side
			//Confirm(MessageIDs.EstablishConnection);

			//Studio side
			SendPath(Studio.path);

			//Celeste side
			//SendCurrentBindings(Hotkeys.listHotkeyKeys);
			Initialized = true;
		}

		public void SendPath(string path) {
			byte[] pathBytes = Encoding.Default.GetBytes(path);
			AddToBuffer(MessageIDs.SendPath, pathBytes);
			WaitForConfirm(MessageIDs.SendPath);
		}

		public void SendHotkeyPressed(HotkeyIDs hotkey) {
			byte[] hotkeyByte = new byte[] { (byte)hotkey };
			AddToBuffer(MessageIDs.SendHotkeyPressed, hotkeyByte);
		}

		public void SendNewBindings(List<Keys> keys) {
			byte[] data = ToByteArray(keys);
			AddToBuffer(MessageIDs.SendNewBindings, data);
			WaitForConfirm(MessageIDs.SendNewBindings);
		}

		public void SendReloadBindings(byte[] data) {
			AddToBuffer(MessageIDs.ReloadBindings, new byte[0]);
			WaitForConfirm(MessageIDs.ReloadBindings);
		}

		#endregion
	}
}
