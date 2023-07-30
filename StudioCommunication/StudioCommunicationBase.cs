﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Threading;

namespace StudioCommunication;

public class StudioCommunicationBase {
    private const int BufferSize = 0x100000;

    // ReSharper disable once MemberCanBePrivate.Global
    protected const int Timeout = 16;

    private static readonly List<StudioCommunicationBase> AttachedCom = new();
    private readonly Mutex mutex;

    //I gave up on using pipes.
    //Don't know whether i was doing something horribly wrong or if .net pipes are just *that* bad.
    //Almost certainly the former.
    private readonly MemoryMappedFile sharedMemory;

    public Func<byte[], bool> ExternalReadHandler;
    private int failedWrites;
    private int lastSignature;

    protected Action PendingWrite;
    private int timeoutCount;
    private bool waiting;

    private static readonly bool runningOnMono = Type.GetType("Mono.Runtime") != null;

    protected StudioCommunicationBase(string target = "CelesteTAS") {
        if (PlatformUtils.Wine || PlatformUtils.NonWindows) {
            string sharedFilePath = Path.Combine("/tmp", $"{target}.share");

            FileStream fs;
            if (File.Exists(sharedFilePath)) {
                fs = new FileStream(sharedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            } else {
                fs = new FileStream(sharedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                fs.SetLength(BufferSize);
            }

            sharedMemory = MemoryMappedFile.CreateFromFile(fs, null, fs.Length, MemoryMappedFileAccess.ReadWrite, null, HandleInheritability.None,
                true);
        } else {
            sharedMemory = MemoryMappedFile.CreateOrOpen(target, BufferSize);
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
        while (true) {
            EstablishConnectionLoop();
            try {
                while (true) {
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