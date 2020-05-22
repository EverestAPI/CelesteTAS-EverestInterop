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
		protected Queue<Message> readQueue = new Queue<Message>();
		protected Queue<Message> writeQueue = new Queue<Message>();
		protected byte[] readBuffer = new byte[BUFFER_SIZE];
		protected const int BUFFER_SIZE = 0x1000;
		protected const int HEADER_LENGTH = 5;
		protected bool waitingForResponse;
		protected AsyncCallback callback;
		public static bool Initialized { get; protected set; }

		protected StudioCommunicationBase() {
			callback = new AsyncCallback(ReadData);
		}

		protected void UpdateLoop() {
			for (; ; ) {
				if (pipe.IsConnected) {
					if (false && readQueue.Count == 0 && !waitingForResponse) {
						ReadMessage();
					}
					if (false && writeQueue.Count > 0 && !waitingForResponse) {
						WriteMessage();
					}
					Thread.Sleep(5);
				}
				else {
					Initialized = false;
					WaitForConnection();
				}
			}
		}

		protected void FillQueue(byte[] buffer) {
			int index = 0;
			while (buffer[index] != 0) {
				MessageIDs id = (MessageIDs)buffer[index];
				byte[] sizeBytes = new byte[4];
				Buffer.BlockCopy(buffer, index + 1, sizeBytes, 0, 4);
				int size = BitConverter.ToInt32(sizeBytes, 0);
				index += HEADER_LENGTH;

				byte[] dataBytes = new byte[size];
				Buffer.BlockCopy(buffer, index, dataBytes, 0, size);

				Message message = new Message(id, dataBytes);
				readQueue.Enqueue(message);
#if DEBUG
				Console.WriteLine($"{this} received {message.ID} with length {message.Length}");
#endif
				index += size;
			}
		}

		protected void ReadMessage() {
			pipe.BeginRead(readBuffer, 0, BUFFER_SIZE, callback, default);
		}

		protected void ReadData(IAsyncResult result) {
			if (result == null)
				return;
			FillQueue(readBuffer);
			readBuffer = new byte[BUFFER_SIZE];
			Message message = readQueue.Dequeue();
			ReadSwitch(message);
		}

		protected virtual void ReadSwitch(Message message) { }

		protected virtual void WaitForConnection() { }

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

		protected void WriteMessage() {
			Message message = writeQueue.Dequeue();
#if DEBUG
			Console.WriteLine($"{this} writing {message.ID} with length {message.Length}");
#endif
			pipe.BeginWrite(message.GetBytes(), 0, message.Length + HEADER_LENGTH, default, default);
			pipe.WaitForPipeDrain();
		}

		protected virtual void EstablishConnection() { }

		protected void Confirm(MessageIDs messageID) {
			writeQueue.Enqueue(new Message(MessageIDs.Confirm, new byte[] { (byte)messageID }));
#if DEBUG
			Console.WriteLine($"{this} confirming {messageID}");
#endif
		}

		protected void WaitForConfirm(MessageIDs messageID) {
			if (writeQueue.Count > 0)
				WriteMessage();
			for (; ; ) {
				pipe.BeginRead(readBuffer, 0, HEADER_LENGTH + 1, default, default);
				FillQueue(readBuffer);
				readBuffer[0] = 0;
				if (readQueue.Count > 0 && readQueue.Peek().ID == MessageIDs.Confirm) {
					if (readQueue.Dequeue().Data[0] == (byte)messageID)
						return;
					throw new Exception();
				}
				else
					readQueue.Clear();
				Thread.Sleep(1);
			}
		}

		protected void WaitForResponse() {
			waitingForResponse = true;
			for (; ; ) {
				pipe.BeginRead(readBuffer, 0, 0x200, default, default);
				FillQueue(readBuffer);
				readBuffer[0] = 0;
				if (readQueue.Count > 0) {
					break;
				}
				Thread.Sleep(1);
			}

			waitingForResponse = false;
			
		}

		//ty stackoverflow
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
	}
}
