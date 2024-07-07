using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if REWRITE

#nullable enable

public abstract class CommunicationAdapterBase : IDisposable {
    protected enum Location { Celeste, Studio }
    
    private bool connected = false;
    public bool Connected {
        get => connected;
        private set {
            if (connected == value) 
                return;
            
            connected = value;
            LogInfo($"Connection changed: {value}");
            OnConnectionChanged();
        }
    }
    
    // Interval for sending Ping messages. Must be greater than TimeoutDelay
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);
    // Amount of time to wait before disconnecting when not receiving messages.
    private static readonly TimeSpan TimeoutDelay = TimeSpan.FromSeconds(3);
    
    private DateTime lastPing = DateTime.UtcNow;
    private DateTime lastMessage = DateTime.UtcNow;
    
    private readonly Mutex mutex;
    
    private readonly Thread thread;
    private bool runThread = true;
    
    /* Memory layout of the communication files:
     *  - Write Offset (4 bytes): Offset for writing new messages
     *  - Message Count (1 byte): Total amount of available messages
     *  - List of messages
     *
     * Message:
     *  - MessageID (1 byte)
     *  - Data (undefined bytes)
     */
    private readonly MemoryMappedFile writeFile;
    private readonly MemoryMappedFile readFile;
    
    private readonly List<(MessageID, Action<BinaryWriter>)> queuedWrites = [];
    
    private const int MessageCountOffset = 4;
    private const int MessagesOffset = MessageCountOffset + 1;
    
    private const int BufferCapacity = 1024 * 1024; // 1MB should be enough for everything
    protected const int UpdateRate = 1000 / 60;

    // Safety caps to avoid any crashes
    private const int MaxOffset = BufferCapacity - 4096;
    private const byte MaxMessageCount = 100;

    private const string MutexName = "Global\\CelesteTAS_StudioCom";
    
    protected CommunicationAdapterBase(Location location) {
        LogInfo("Starting communication...");
        
        // Get or create the shared mutex
        mutex = new Mutex(initiallyOwned: false, MutexName, out bool created);
        if (!created) {
            mutex = Mutex.OpenExisting(MutexName);
        }
        
        // Set up the memory mapped files
        string writeName = $"CelesteTAS_{(location == Location.Celeste ? "C2S" : "S2C")}";
        string readName  = $"CelesteTAS_{(location == Location.Celeste ? "S2C" : "C2S")}";
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            writeFile = MemoryMappedFile.CreateOrOpen(writeName, BufferCapacity);
            readFile = MemoryMappedFile.CreateOrOpen(readName, BufferCapacity);
        } else {
            var writePath = Path.Combine(Path.GetTempPath(), $"{writeName}.share");
            var readPath  = Path.Combine(Path.GetTempPath(), $"{readName}.share");
            
            using var writeFs = File.Open(writePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var readFs = File.Open(readPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            
            writeFile = MemoryMappedFile.CreateFromFile(writeFs, null, BufferCapacity, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
            readFile = MemoryMappedFile.CreateFromFile(readFs, null, BufferCapacity, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
        }
        
        // Clean-up old data (only the header is important)
        mutex.WaitOne();
        using (var writeStream = writeFile.CreateViewStream()) {
            writeStream.Position = 0;
            writeStream.Write([0x00, 0x00, 0x00, 0x00, 0x00]);    
        }
        using (var readStream = readFile.CreateViewStream()) {
            readStream.Position = 0;
            readStream.Write([0x00, 0x00, 0x00, 0x00, 0x00]);    
        }
        mutex.ReleaseMutex();
        
        // Start the communication thread
        thread = new Thread(() => {
            var lastCrash = DateTime.UtcNow;
            
            Retry:
            try {
                UpdateThread();
            } catch (Exception ex) {
                LogError($"Thread crashed: {ex}");
                
                var now = DateTime.UtcNow;
                if (now - lastCrash < TimeSpan.FromSeconds(5)) {
                    // "try turning it off and on again"
                    LogError("Thread crashed again within 5 seconds. Resetting communication...");
                    Task.Run(FullReset);
                    return;
                }
                
                // Restart the thread when it crashed
                lastCrash = now;
                goto Retry;
            }
        }) {
            Name = "StudioCom"
        };
        thread.Start();
        LogInfo("Communication started");
    }
    public void Dispose() {
        LogInfo("Stopping communication...");
        GC.SuppressFinalize(this);
        
        runThread = false;
        thread.Join();
        
        writeFile.Dispose();
        readFile.Dispose();
        
        mutex.Dispose();
        LogInfo("Communication stopped");
    }
    
    /// Main thread of the studio communication.
    /// Reads all messages which have been sent and writes any queued messages.
    private void UpdateThread() {
        bool mutexAcquired = false;

        try {
            while (runThread) {
                Thread.Sleep(UpdateRate);
                
                var now = DateTime.UtcNow;
                mutex.WaitOne();
                mutexAcquired = true;
                
                // Read
                {
                    using var readStream = readFile.CreateViewStream();
                    using var reader = new BinaryReader(readStream);
                    
                    readStream.Seek(MessageCountOffset, SeekOrigin.Begin);
                    byte count = reader.ReadByte();
                    
                    // Handle timeout
                    if (count != 0) {
                        lastMessage = now;
                        Connected = true;
                    } else if (now - lastMessage > TimeoutDelay) {
                        Connected = false;
                    }
                    
                    if (Connected) {
                        // Read all available messages
                        for (byte i = 0; i < Math.Min(count, MaxMessageCount); i++) {
                            if (readStream.Position >= MaxOffset) {
                                break;
                            }
                            
                            var messageId = (MessageID)reader.ReadByte();
                            if (messageId == MessageID.None) {
                                LogError("Messages ended early! Something probably got corrupted!");
                                break;
                            } else if (messageId == MessageID.Ping) {
                                // Just sent to keep up the connection
                            } else if (messageId == MessageID.Reset) {
                                LogVerbose("Received message Reset");
                                // Fully restart ourselves. Called async to avoid deadlocks
                                Connected = false;
                                Task.Run(FullReset);
                                return;
                            } else {
                                HandleMessage(messageId, reader);
                            }
                        }
                        
                        // Reset write offset and message count
                        Debug.Assert(MessagesOffset == 5);
                        readStream.Position = 0;
                        readStream.Write([0x00, 0x00, 0x00, 0x00, 0x00], 0, 5);
                    }
                }
                
                // Write
                {
                    if (Connected) {
                        // Write queued messages
                        lock(queuedWrites) {
                            foreach (var (messageId, serialize) in queuedWrites) {
                                WriteMessage(messageId, serialize);
                            }
                            queuedWrites.Clear();
                        }
                    }
                    
                    // Only send ping when there aren't any other messages (so they aren't spammed)
                    if (now - lastPing > PingInterval) {
                        using var writeStream = writeFile.CreateViewStream();
                        using var reader = new BinaryReader(writeStream);
                        using var writer = new BinaryWriter(writeStream);
                        
                        // Set current write offset / message count
                        writeStream.Position = MessageCountOffset;
                        byte count = reader.ReadByte();
                        
                        if (count == 0) {
                            // The Ping message has no data attached and there aren't any other messages
                            writeStream.Position = 0;
                            writer.Write(1);
                            writer.Write((byte)1);
                            writer.Write((byte)MessageID.Ping);
                        }
                        
                        lastPing = now;
                    }
                }
                
                mutexAcquired = false;
                mutex.ReleaseMutex();
            }
        } finally {
            // Always make sure to release the mutex again
            if (mutexAcquired) {
                mutex.ReleaseMutex();
            }
        }
    }
    
    /// Queues the message to be sent with the next update cycle.
    protected void QueueMessage(MessageID messageId, Action<BinaryWriter> serialize) {
        lock(queuedWrites) {
            queuedWrites.Add((messageId, serialize));
        }
    }
    /// Immediately writes the message, blocking until it is written.
    protected void WriteMessageNow(MessageID messageId, Action<BinaryWriter> serialize) {
        mutex.WaitOne();
        WriteMessage(messageId, serialize);
        mutex.ReleaseMutex();
    }
    
    private void WriteMessage(MessageID messageId, Action<BinaryWriter> serialize) {
        using var writeStream = writeFile.CreateViewStream();
        using var reader = new BinaryReader(writeStream, Encoding.UTF8);
        using var writer = new BinaryWriter(writeStream);
        
        // Set current write offset / check message count
        writeStream.Position = 0;
        
        int offset = reader.ReadInt32() + MessagesOffset;
        byte count = reader.ReadByte();
        
        if (offset >= MaxOffset || count >= MaxMessageCount) {
            // The other process probably was disconnected, but the timeout isn't done yet
            return;
        }
        
        writeStream.Position = MessageCountOffset;
        writer.Write((byte)(count + 1));
        
        writeStream.Position = offset;
        writer.Write((byte)messageId);
        
        serialize(writer);
        
        int newOffset = (int)writeStream.Position - MessagesOffset;
        writeStream.Position = 0;
        writeStream.Write(BitConverter.GetBytes(newOffset));
    }
    
    protected abstract void FullReset();
    protected abstract void OnConnectionChanged();
    protected abstract void HandleMessage(MessageID messageId, BinaryReader reader);
    
    protected abstract void LogInfo(string message);
    protected abstract void LogVerbose(string message);
    protected abstract void LogError(string message);
}

#else

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

#nullable disable

namespace StudioCommunication;

public class StudioCommunicationBase {
    private static readonly bool runningOnMono = Type.GetType("Mono.Runtime") != null;

    private const int BufferSize = 0x100000;

    // ReSharper disable once MemberCanBePrivate.Global
    protected const int Timeout = 16;

    private static readonly List<StudioCommunicationBase> AttachedCom = new();
    private readonly Mutex mutex;
    
    public event Action Reset;

    //I gave up on using pipes.
    //Don't know whether i was doing something horribly wrong or if .net pipes are just *that* bad.
    //Almost certainly the former.
    private readonly MemoryMappedFile sharedMemory;

    public Func<byte[], bool> ExternalReadHandler;
    private int failedWrites;
    private int lastSignature;

    protected Action PendingWrite;
    protected bool Destroyed;
    private int timeoutCount;
    private bool waiting;

    protected StudioCommunicationBase(string target = "CelesteTAS") {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            sharedMemory = MemoryMappedFile.CreateOrOpen(target, BufferSize);
        } else {
            string sharedFilePath = Path.Combine(Path.GetTempPath(), $"{target}.share");

            FileStream fs;
            if (File.Exists(sharedFilePath))
            {
                fs = new FileStream(sharedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }
            else
            {
                fs = new FileStream(sharedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                fs.SetLength(BufferSize);
            }

            sharedMemory = MemoryMappedFile.CreateFromFile(fs, null, fs.Length, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, true);
        }

        string mutexName = $"{target}_Mutex";
        mutex = new Mutex(false, mutexName, out bool created);
        if (!created) {
            mutex = Mutex.OpenExisting(mutexName);
        }

        AttachedCom.Add(this);
    }

    public static bool Initialized { get; protected set; }

    ~StudioCommunicationBase() {
        sharedMemory.Dispose();
        mutex.Dispose();
    }

    protected void UpdateLoop() {
        while (!Destroyed) {
            EstablishConnectionLoop();
            try {
                while (!Destroyed) {
                    Message? message = ReadMessage();

                    if (message != null) {
                        ReadData((Message) message);
                        waiting = false;
                    }

                    Thread.Sleep(Timeout);

                    if (!NeedsToWait()) {
                        PendingWrite?.Invoke();
                        PendingWrite = null;
                    }
                }
            }
            //For this to work all writes must occur in this thread
            catch (NeedsResetException e) {
                ForceReset(e);
            }
        }
    }

    protected virtual bool NeedsToWait() => waiting;

    private bool IsHighPriority(MessageID id) =>
        Attribute.IsDefined(typeof(MessageID).GetField(Enum.GetName(typeof(MessageID), id)), typeof(HighPriorityAttribute));

    protected Message? ReadMessage() {
        MessageID id = default;
        int signature;
        int size;
        byte[] data;

        using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
            mutex.WaitOne();
            //Log($"{this} acquired mutex for read");

            BinaryReader reader = new(stream);
            BinaryWriter writer = new(stream);

            id = (MessageID) reader.ReadByte();
            if (id == MessageID.Default) {
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
            writer.Write((byte) 0);

            mutex.ReleaseMutex();
        }


        Message message = new(id, data);
        if (message.Id != MessageID.SendState && message.Id != MessageID.SendHotkeyPressed) {
            Log($"{this} received {message.Id} with length {message.Length}");
        }

        return message;
    }

    protected Message ReadMessageGuaranteed() {
        Log($"{this} forcing read");
        int failedReads = 0;
        while (true) {
            Message? message = ReadMessage();
            if (message != null) {
                return (Message) message;
            }

            if ( /*Initialized &&*/ ++failedReads > 100) {
                throw new NeedsResetException("Read timed out");
            }

            Thread.Sleep(Timeout);
        }
    }

    protected bool WriteMessage(Message message, bool local = true) {
        if (!local) {
            foreach (var com in AttachedCom) {
                if (com != this) {
                    com.PendingWrite = com.PendingWrite ?? (() => WriteMessage(message));
                }
            }
        }

        using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
            mutex.WaitOne();

            //Log($"{this} acquired mutex for write");
            BinaryReader reader = new(stream);
            BinaryWriter writer = new(stream);

            //Check that there isn't a message waiting to be read
            byte firstByte = reader.ReadByte();
            if (firstByte != 0 && (!IsHighPriority(message.Id) || IsHighPriority((MessageID) firstByte))) {
                mutex.ReleaseMutex();
                if ( /*Initialized &&*/ ++failedWrites > 100) {
                    throw new NeedsResetException("Write timed out");
                }

                return false;
            }

            if (message.Id != MessageID.SendState && message.Id != MessageID.SendHotkeyPressed) {
                Log($"{this} writing {message.Id} with length {message.Length}");
            }

            stream.Position = 0;
            writer.Write(message.GetBytes());

            mutex.ReleaseMutex();
        }

        lastSignature = Message.Signature;
        failedWrites = 0;
        return true;
    }

    protected void WriteMessageGuaranteed(Message message, bool local = true) {
        if (!local) {
            foreach (var com in AttachedCom) {
                if (com != this) {
                    com.PendingWrite = () => WriteMessageGuaranteed(message);
                }
            }
        }

        while (true) {
            if (WriteMessage(message)) {
                break;
            }

            Thread.Sleep(Timeout);
        }
    }

    protected void ForceReset(NeedsResetException e) {
        Initialized = false;
        waiting = false;
        failedWrites = 0;
        PendingWrite = null;
        timeoutCount++;
        Log($"Exception thrown - {e.Message}");
        Reset?.Invoke();
        //Ensure the first byte of the mmf is reset
        using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
            mutex.WaitOne();
            BinaryWriter writer = new(stream);
            writer.Write((byte) 0);
            mutex.ReleaseMutex();
        }

        WriteReset();
        Thread.Sleep(Timeout * 2);
    }

    //Only needs to be used on the Celeste end, as Celeste will detect disconnects much faster
    protected virtual void WriteReset() {
        using (MemoryMappedViewStream stream = sharedMemory.CreateViewStream()) {
            mutex.WaitOne();
            BinaryWriter writer = new(stream);
            Message reset = new(MessageID.Reset, new byte[0]);
            writer.Write(reset.GetBytes());
            mutex.ReleaseMutex();
        }
    }

    public void WriteWait() {
        PendingWrite = () => WriteMessageGuaranteed(new Message(MessageID.Wait, new byte[0]));
    }

    protected void ProcessWait() {
        waiting = true;
    }

    protected virtual void ReadData(Message message) { }

    private void EstablishConnectionLoop() {
        while (true) {
            try {
                EstablishConnection();
                timeoutCount = 0;
                break;
            } catch (NeedsResetException e) {
                ForceReset(e);
            }
        }
    }

    protected virtual void EstablishConnection() { }

    public override string ToString() {
        string location = Assembly.GetExecutingAssembly().GetName().Name;
        return $"StudioCommunicationBase Location @ {location}";
    }

    // ReSharper disable once MemberCanBePrivate.Global
    protected void Log(string log) {
        if (timeoutCount <= 5) {
            LogImpl(log);
        }
    }

    protected virtual void LogImpl(string text) {
        Console.WriteLine(text);
    }

    // This is literally the first thing I have ever written with threading
    // Apologies in advance to anyone else working on this

    // ReSharper disable once StructCanBeMadeReadOnly
    public struct Message {
        public MessageID Id { get; }
        public byte[] Data { get; }
        public int Length => Data.Length;

        public static readonly int Signature = Thread.CurrentThread.GetHashCode();
        private const int HeaderLength = 9;

        public Message(MessageID id, byte[] data) {
            Id = id;
            Data = data;
        }

        public byte[] GetBytes() {
            byte[] bytes = new byte[Length + HeaderLength];
            bytes[0] = (byte) Id;
            Buffer.BlockCopy(BitConverter.GetBytes(Signature), 0, bytes, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(Length), 0, bytes, 5, 4);
            Buffer.BlockCopy(Data, 0, bytes, HeaderLength, Length);
            return bytes;
        }
    }

    protected class NeedsResetException : Exception {
        public NeedsResetException() { }
        public NeedsResetException(string message) : base(message) { }
    }
}

#endif