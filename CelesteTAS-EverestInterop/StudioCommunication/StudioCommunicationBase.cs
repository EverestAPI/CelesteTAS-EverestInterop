using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TAS.StudioCommunication {
	public class StudioCommunicationBase {
		// This is literally the first thing I have ever written with threading
		// Apologies in advance to anyone else working on this

		protected struct Message {
			public MessageIDs ID { get; private set; }
			public int Length { get; private set; }
			public byte[] Data { get; private set; }

			public Message(MessageIDs id, byte[] data) {
				ID = id;
				Data = data;
				Length = data.Length;
			}

			public byte[] GetBytes() {
				byte[] bytes = new byte[Length + HEADER_LENGTH];
				bytes[0] = (byte)ID;
				Buffer.BlockCopy(BitConverter.GetBytes(Length), 0, bytes, 1, 4);
				Buffer.BlockCopy(Data, 0, bytes, HEADER_LENGTH, Length);
				return bytes;
			}
		}

		protected PipeStream pipe;
		//protected Queue<Message> writeQueue = new Queue<Message>();
		protected byte[] readBuffer = new byte[BUFFER_SIZE];
		protected const int BUFFER_SIZE = 0x1000;
		protected const int HEADER_LENGTH = 5;
		protected bool waitingForResponse;
		protected CancellationTokenSource pendingRead;
		protected CancellationTokenSource pendingWrite;
		public static bool Initialized { get; protected set; }

		protected StudioCommunicationBase() {
			//callback = new AsyncCallback(ReadData);
		}

		protected void UpdateLoop() {
			for (; ; ) {
				if (pipe.IsConnected) {
					if (false && !waitingForResponse) {
						//ReadSwitch(await ReadMessage());
					}
					//if (false && writeQueue.Count > 0 && !waitingForResponse) {
					//	WriteMessage();
					//}
					Thread.Sleep(5);
				}
				else {
					Initialized = false;
					WaitForConnection();
				}
			}
		}

		protected async Task<Message> ReadMessage() {
			Log($"{this} attempting read");

			MessageIDs id = default;
			while (id == default) {
				pendingRead = new CancellationTokenSource();

				await Task.Run(() => pipe.ReadAsync(readBuffer, 0, BUFFER_SIZE), pendingRead.Token);
				id = (MessageIDs)readBuffer[0];
			}

			byte[] sizeBytes = new byte[4];
			Buffer.BlockCopy(readBuffer, 1, sizeBytes, 0, 4);
			int size = BitConverter.ToInt32(sizeBytes, 0);

			byte[] dataBytes = new byte[size];
			Buffer.BlockCopy(readBuffer, HEADER_LENGTH, dataBytes, 0, size);

			Message message = new Message(id, dataBytes);
			Log($"{this} received {message.ID} with length {message.Length}");

			readBuffer[0] = 0;
			return message;
		}

		protected async void WriteMessage(Message message) {

			if (pendingRead != null) {
				Log($"{this} cancelling read");
				pendingRead?.Cancel();
				pendingRead?.Dispose();
			}

			Log($"{this} attempting to write {message.ID} with length {message.Length}");

			pendingWrite = new CancellationTokenSource();

			await Task.Run(() => pipe.WriteAsync(readBuffer, 0, message.Length + HEADER_LENGTH), pendingWrite.Token);
			
			Log($"{this} wrote {message.ID} with length {message.Length}");
			//pipe.BeginWrite(message.GetBytes(), 0, message.Length + HEADER_LENGTH, default, default);
			await pipe.FlushAsync();
			//pipe.WaitForPipeDrain();
		}

		protected void ReadData(IAsyncResult result) {
			ReadSwitch(ReadMessage().Result);
		}

		protected virtual void ReadSwitch(Message message) { }

		protected virtual void WaitForConnection() { }

		protected virtual void EstablishConnection() { }

		protected void Confirm(MessageIDs messageID) {
			WriteMessage(new Message(MessageIDs.Confirm, new byte[] { (byte)messageID }));
		}

		protected void WaitForConfirm(MessageIDs messageID) {
			while (pendingWrite != null)
				Thread.Sleep(1);
			Message message = ReadMessage().Result;
			if (message.ID == MessageIDs.Confirm) {
				if (message.Data[0] == (byte)messageID)
					return;
				throw new Exception();
			}
		}

		//ty stackoverflow
		protected T FromByteArray<T>(byte[] data, int offset = 0, int length = 0) {
			if (data == null)
				return default(T);
			if (length == 0)
				length = data.Length - offset;
			BinaryFormatter bf = new BinaryFormatter();
			using (MemoryStream ms = new MemoryStream(data, offset, length)) {
				object obj = bf.Deserialize(ms);
				return (T)obj;
			}
		}

		protected byte[] ToByteArray<T>(T obj) {
			if (obj == null)
				return new byte[0];
			BinaryFormatter bf = new BinaryFormatter();
			using (MemoryStream ms = new MemoryStream()) {
				bf.Serialize(ms, obj);
				return ms.ToArray();
			}
		}

		public override string ToString() {
			string pipeType = (pipe is NamedPipeClientStream) ? "Client" : "Server";
			string location = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
			return $"{pipeType} @ {location}";
		}

		protected void Log(string s) {
#if DEBUG
			//Console.ForegroundColor = (pipe is NamedPipeClientStream) ? ConsoleColor.Green : ConsoleColor.Cyan;
			Console.WriteLine(s);
			Console.ResetColor();
#endif
		}
	}
}
