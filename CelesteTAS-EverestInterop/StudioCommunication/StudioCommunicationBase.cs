using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TAS.StudioCommunication {
	public class StudioCommunicationBase {
		protected PipeStream pipe;
		protected byte[] readBuffer = new byte[BUFFER_SIZE];
		protected byte[] writeBuffer = new byte[BUFFER_SIZE];
		protected const int BUFFER_SIZE = 0x200;
		protected const int HEADER_LENGTH = 5;
		protected bool waitingForResponse;
		public static bool Initialized { get; protected set; }

		protected void UpdateLoop() {
			AsyncCallback callback = new AsyncCallback(ReadData);
			for (; ; ) {
				if (pipe.IsConnected) {
					if (readBuffer[0] == 0) {
						pipe.BeginRead(readBuffer, 0, 0x200, callback, default);
					}
					else {
						ReadData(null);
					}
					if (writeBuffer[0] != 0 && !waitingForResponse) {
						pipe.BeginWrite(writeBuffer, 0, writeBuffer.Length, default, default);
					}
				}
				else {
					Initialized = false;
					WaitForConnection();
				}
			}
		}

		protected int LengthOfMessage(byte[] buffer, int index = 0) {
			if (buffer[index] == 0)
				return 0;
			byte[] sizeBytes = new byte[4];
			Buffer.BlockCopy(buffer, 1, sizeBytes, 0, 4);
			int size = BitConverter.ToInt32(sizeBytes, 0);
			return size;
		}

		protected virtual void ReadData(IAsyncResult result) { }

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

		protected void AddToBuffer(MessageIDs messageID, byte[] data) {
			int index = 0;
			int size;
			do {
				size = LengthOfMessage(writeBuffer, index);
				if (size > 0)
					index += HEADER_LENGTH + size;
			} while (size > 0);

			Buffer.SetByte(writeBuffer, index, (byte)messageID);
			Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, writeBuffer, index + 1, 4);
			index += HEADER_LENGTH;
			Buffer.BlockCopy(data, 0, writeBuffer, index, data.Length);
		}

		protected virtual void EstablishConnection() { }

		protected void Confirm(MessageIDs messageID, bool needsResponse = false) {
			AddToBuffer(MessageIDs.Confirm, new byte[] { (byte)messageID });
			waitingForResponse = needsResponse;
		}

		protected void WaitForConfirm(MessageIDs messageID) {
			for (; ; ) {
				pipe.BeginRead(readBuffer, 0, 0x200, default, default);
				int size = LengthOfMessage(readBuffer);
				if (size > 0 && readBuffer[0] == (byte)MessageIDs.Confirm) {
					if (readBuffer[HEADER_LENGTH] == (byte)messageID)
						return;
					throw new Exception();
				}
				Thread.Sleep(1);
			}
		}

		//ty stackoverflow
		protected byte[] ToByteArray<T>(T obj) {
			if (obj == null)
				return null;
			BinaryFormatter bf = new BinaryFormatter();
			using (MemoryStream ms = new MemoryStream()) {
				bf.Serialize(ms, obj);
				return ms.ToArray();
			}
		}
	}
}
