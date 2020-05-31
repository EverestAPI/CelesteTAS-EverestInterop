using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net.Security;
using System.Reflection;
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

			public static readonly int Signature = Assembly.GetExecutingAssembly().GetHashCode();

			public Message(MessageIDs id, byte[] data) {
				ID = id;
				Data = data;
				Length = data.Length;
			}

			public byte[] GetBytes() {
				byte[] bytes = new byte[Length + HEADER_LENGTH];
				bytes[0] = (byte)ID;
				Buffer.BlockCopy(BitConverter.GetBytes(Signature), 0, bytes, 1, 4);
				Buffer.BlockCopy(BitConverter.GetBytes(Length), 0, bytes, 5, 4);
				Buffer.BlockCopy(Data, 0, bytes, HEADER_LENGTH, Length);
				return bytes;
			}

		}

		protected class NeedsResetException : Exception {
			public NeedsResetException() { }
			public NeedsResetException(string message) : base(message) { }
		}

		//I gave up on using pipes.
		//Don't know whether i was doing something horribly wrong or if .net pipes are just *that* bad.
		//Almost certainly the former.
		private MemoryMappedFile sharedMemory;
		private Mutex mutex;
		private int lastSignature;
		protected int timeout = 16;
		private int failedWrites = 0;
		private int timeoutCount = 0;
		private bool waiting;

		protected Action pendingWrite;

		protected const int BUFFER_SIZE = 0x1000;
		protected const int HEADER_LENGTH = 9;
		public static bool Initialized { get; protected set; }

		protected StudioCommunicationBase() {
			sharedMemory = MemoryMappedFile.CreateOrOpen("CelesteTAS", BUFFER_SIZE);
			mutex = new Mutex(false, "CelesteTASCOM", out bool created);
			if (!created)
				mutex = Mutex.OpenExisting("CelesteTASCOM");
		}

		~StudioCommunicationBase() {
			sharedMemory.Dispose();
			mutex.Dispose();
		}

		protected void UpdateLoop() {
			for (; ; ) {
				EstablishConnectionLoop();
				try {
					for (; ; ) {
						Message? message = ReadMessage();

						if (message != null) {
							ReadData((Message)message);
							waiting = false;
						}
						Thread.Sleep(timeout);

						if (!waiting) {
							pendingWrite?.Invoke();
							pendingWrite = null;
						}
					}
				}
				//For this to work all writes must occur in this thread
				catch (NeedsResetException e) {
					ForceReset(e);
				}
			}
		}

		private bool IsHighPriority(MessageIDs ID) =>
			Attribute.IsDefined(typeof(MessageIDs).GetField(Enum.GetName(typeof(MessageIDs), ID)), typeof(HighPriorityAttribute));

		protected Message? ReadMessage() {

			MessageIDs id = default;
			int signature;
			int size;
			byte[] data;

			using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
				mutex.WaitOne();
				//Log($"{this} acquired mutex for read");

				BinaryReader reader = new BinaryReader(stream);
				BinaryWriter writer = new BinaryWriter(stream);

				id = (MessageIDs)reader.ReadByte();
				if (id == MessageIDs.Default) {
					mutex.ReleaseMutex();
					return null;
				}
				//Make sure the message came from the other side
				signature = reader.ReadInt32();
				if (signature == lastSignature) {
					mutex.ReleaseMutex();
					return null;
				}
				size = reader.ReadInt32();
				data = reader.ReadBytes(size);

				//Overwriting the first byte ensures that the data will only be read once
				stream.Position = 0;
				writer.Write((byte)0);

				mutex.ReleaseMutex();
			}


			Message message = new Message(id, data);
			if (message.ID != MessageIDs.SendState && message.ID != MessageIDs.SendHotkeyPressed)
				Log($"{this} received {message.ID} with length {message.Length}");

			return message;
		}

		protected Message ReadMessageGuaranteed() {
			Log($"{this} forcing read");
			int failedReads = 0;
			for (; ; ) {
				Message? message = ReadMessage();
				if (message != null)
					return (Message)message;
				if (/*Initialized &&*/ ++failedReads > 100)
					throw new NeedsResetException("Read timed out");
				Thread.Sleep(timeout);
			}
		}

		protected bool WriteMessage(Message message) {

			using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
				mutex.WaitOne();

				//Log($"{this} acquired mutex for write");
				BinaryReader reader = new BinaryReader(stream);
				BinaryWriter writer = new BinaryWriter(stream);

				//Check that there isn't a message waiting to be read
				byte firstByte = reader.ReadByte();
				if (firstByte != 0 && (!IsHighPriority(message.ID) || IsHighPriority((MessageIDs)firstByte))) {

					mutex.ReleaseMutex();
					if (/*Initialized &&*/ ++failedWrites > 100) {
						throw new NeedsResetException("Write timed out");
					}
					return false;
				}

				if (message.ID != MessageIDs.SendState && message.ID != MessageIDs.SendHotkeyPressed)
					Log($"{this} writing {message.ID} with length {message.Length}");

				stream.Position = 0;
				writer.Write(message.GetBytes());

				mutex.ReleaseMutex();
			}

			lastSignature = Message.Signature;
			failedWrites = 0;
			return true;
		}

		protected void WriteMessageGuaranteed(Message message) {

			if (message.ID != MessageIDs.SendState)
				Log($"{this} forcing write of {message.ID} with length {message.Length}");

			for (; ; ) {
				if (WriteMessage(message))
					break;
				Thread.Sleep(timeout);
			}
		}

		protected void ForceReset(NeedsResetException e) {
			Initialized = false;
			waiting = false;
			failedWrites = 0;
			pendingWrite = null;
			timeoutCount++;
			Log($"Exception thrown - {e.Message}");
			//Ensure the first byte of the mmf is reset
			using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
				mutex.WaitOne();
				BinaryWriter writer = new BinaryWriter(stream);
				writer.Write((byte)0);
				mutex.ReleaseMutex();
			}
			//Only for Celeste
			WriteReset();
			Thread.Sleep(timeout * 2);
		}
		
		protected void WriteReset() {
			using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
				mutex.WaitOne();
				BinaryWriter writer = new BinaryWriter(stream);
				Message reset = new Message(MessageIDs.Reset, new byte[0]);
				writer.Write(reset.GetBytes());
				mutex.ReleaseMutex();
			}
		}

		public void WriteWait() {
			pendingWrite = () => WriteMessageGuaranteed(new Message(MessageIDs.Wait, new byte[0]));
		}

		protected void ProcessWait() {
			waiting = true;
		}

		protected virtual void ReadData(Message message) { }

		private void EstablishConnectionLoop() {
			for (; ;) {
				try {
					EstablishConnection();
					timeoutCount = 0;
					break;
				}
				catch (NeedsResetException e) {
					ForceReset(e);
				}
			}
		}

		protected virtual void EstablishConnection() { }

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
			//string pipeType = (this is StudioCommunicationServer) ? "Server" : "Client";
			string location = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
			return $"Client @ {location}";
		}

		protected void Log(string s) {
//#if DEBUG
			if (timeoutCount <= 5)
				Console.WriteLine(s);
//#endif
		}
	}
}
