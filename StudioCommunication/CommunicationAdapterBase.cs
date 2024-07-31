using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace StudioCommunication;

public abstract class CommunicationAdapterBase : IDisposable {
    protected enum Location { Celeste, Studio }
    
    private bool connected = false;
    public bool Connected {
        get => connected;
        private set {
            if (connected == value) 
                return;
            
            connected = value;
            LogInfo(connected ? "Connected" : "Disconnected");
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
        
        // Ensure the other adapter isn't using the stream while setting up
        try {
            mutex.WaitOne();
        } catch (AbandonedMutexException) {
            // The other adapter most likely exited abnormally
            mutex.Dispose();
            mutex = new Mutex(initiallyOwned: true, MutexName, out _);
        }
        
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
                if (ex is AbandonedMutexException) {
                    // Reset communication to try and recover
                    Task.Run(FullReset);
                    return;
                }
                
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
