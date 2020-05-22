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
			updateThread.Name = "StudioCom Server";
			updateThread.Start();
		}

		protected override void WaitForConnection() {
			(pipe as NamedPipeServerStream).WaitForConnection();
			ThreadStart establishConnection = new ThreadStart(EstablishConnection);
			Thread thread = new Thread(establishConnection);
			thread.Name = "Server Initialization";
			thread.Start();
		}

		#region Read
		protected override void ReadSwitch(Message message) {
			switch (message.ID) {
				case MessageIDs.SendState:
					ProcessSendState(message.Data);
					break;
				case MessageIDs.SendPlayerData:
					ProcessSendPlayerData(message.Data);
					break;
				case MessageIDs.SendCurrentBindings:
					ProcessSendCurrentBindings(message.Data);
					break;
				default:
					throw new InvalidOperationException();
			}
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
			writeQueue.Enqueue(new Message(MessageIDs.EstablishConnection, new byte[0]));
			WaitForConfirm(MessageIDs.EstablishConnection);

			//Celeste side
			//WaitForResponse();
			//Confirm(MessageIDs.EstablishConnection);
			//WaitForResponse();

			//Studio side
			SendPath(Studio.path);

			//Celeste side
			//SendCurrentBindings(Hotkeys.listHotkeyKeys);

			Initialized = true;
		}

		public void SendPath(string path) {
			byte[] pathBytes = Encoding.Default.GetBytes(path);
			writeQueue.Enqueue(new Message(MessageIDs.SendPath, pathBytes));
			WaitForConfirm(MessageIDs.SendPath);
		}

		public void SendHotkeyPressed(HotkeyIDs hotkey) {
			byte[] hotkeyByte = new byte[] { (byte)hotkey };
			writeQueue.Enqueue(new Message(MessageIDs.SendHotkeyPressed, hotkeyByte));
		}

		public void SendNewBindings(List<Keys> keys) {
			byte[] data = ToByteArray(keys);
			writeQueue.Enqueue(new Message(MessageIDs.SendNewBindings, data));
			WaitForConfirm(MessageIDs.SendNewBindings);
		}

		public void SendReloadBindings(byte[] data) {
			writeQueue.Enqueue(new Message(MessageIDs.ReloadBindings, new byte[0]));
			WaitForConfirm(MessageIDs.ReloadBindings);
		}

		#endregion
	}
}
