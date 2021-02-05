using System;
using System.Diagnostics;

namespace CelesteStudio.Entities {
public enum PointerVersion {
    XNA,
    OpenGL,
    Itch
}

public enum AutoDeref {
    None,
    Single,
    Double
}

public class ProgramSignature {
    public ProgramSignature(PointerVersion version, string signature, int offset) {
        Version = version;
        Signature = signature;
        Offset = offset;
    }

    public PointerVersion Version { get; set; }
    public string Signature { get; set; }
    public int Offset { get; set; }

    public override string ToString() {
        return Version.ToString() + " - " + Signature;
    }
}

public class ProgramPointer {
    private readonly int[] offsets;
    private readonly ProgramSignature[] signatures;
    private int lastID;
    private DateTime lastTry;

    public ProgramPointer(AutoDeref autoDeref, params ProgramSignature[] signatures) {
        AutoDeref = autoDeref;
        this.signatures = signatures;
        lastID = -1;
        lastTry = DateTime.MinValue;
    }

    public ProgramPointer(AutoDeref autoDeref, params int[] offsets) {
        AutoDeref = autoDeref;
        this.offsets = offsets;
        lastID = -1;
        lastTry = DateTime.MinValue;
    }

    public IntPtr Pointer { get; private set; }
    public PointerVersion Version { get; private set; }
    public AutoDeref AutoDeref { get; private set; }

    public T Read<T>(Process program, params int[] offsets) where T : struct {
        GetPointer(program);
        return program.Read<T>(Pointer, offsets);
    }

    public string Read(Process program, params int[] offsets) {
        GetPointer(program);
        return program.Read(Pointer, offsets);
    }

    public byte[] ReadBytes(Process program, int length, params int[] offsets) {
        GetPointer(program);
        return program.Read(Pointer, length, offsets);
    }

    public void Write<T>(Process program, T value, params int[] offsets) where T : struct {
        GetPointer(program);
        program.Write<T>(Pointer, value, offsets);
    }

    public void Write(Process program, byte[] value, params int[] offsets) {
        GetPointer(program);
        program.Write(Pointer, value, offsets);
    }

    public void ClearPointer() {
        Pointer = IntPtr.Zero;
    }

    public IntPtr GetPointer(Process program) {
        if (program == null) {
            Pointer = IntPtr.Zero;
            lastID = -1;
            return Pointer;
        } else if (program.Id != lastID) {
            Pointer = IntPtr.Zero;
            lastID = program.Id;
        }

        if (Pointer == IntPtr.Zero && DateTime.Now > lastTry.AddSeconds(1)) {
            lastTry = DateTime.Now;

            Pointer = GetVersionedFunctionPointer(program);
            if (Pointer != IntPtr.Zero) {
                if (AutoDeref != AutoDeref.None) {
                    if (MemoryReader.is64Bit) {
                        Pointer = (IntPtr) program.Read<ulong>(Pointer);
                    } else {
                        Pointer = (IntPtr) program.Read<uint>(Pointer);
                    }

                    if (AutoDeref == AutoDeref.Double) {
                        if (MemoryReader.is64Bit) {
                            Pointer = (IntPtr) program.Read<ulong>(Pointer);
                        } else {
                            Pointer = (IntPtr) program.Read<uint>(Pointer);
                        }
                    }
                }
            }
        }

        return Pointer;
    }

    private IntPtr GetVersionedFunctionPointer(Process program) {
        if (signatures != null) {
            MemorySearcher searcher = new MemorySearcher();
            searcher.MemoryFilter = delegate(MemInfo info) { return (info.State & 0x1000) != 0 && (info.Protect & 0x40) != 0 && (info.Protect & 0x100) == 0; };
            for (int i = 0; i < signatures.Length; i++) {
                ProgramSignature signature = signatures[i];

                IntPtr ptr = searcher.FindSignature(program, signature.Signature);
                if (ptr != IntPtr.Zero) {
                    Version = signature.Version;
                    return ptr + signature.Offset;
                }
            }

            return IntPtr.Zero;
        }

        if (MemoryReader.is64Bit) {
            return (IntPtr) program.Read<ulong>(program.MainModule.BaseAddress, offsets);
        } else {
            return (IntPtr) program.Read<uint>(program.MainModule.BaseAddress, offsets);
        }
    }
}
}