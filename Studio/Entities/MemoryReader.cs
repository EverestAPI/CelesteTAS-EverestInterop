using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CelesteStudio.Entities {
public static class MemoryReader {
    private static readonly Dictionary<int, Module64[]> ModuleCache = new Dictionary<int, Module64[]>();
    public static bool is64Bit;

    public static void Update64Bit(Process program) {
        is64Bit = program.Is64Bit();
    }

    public static T Read<T>(this Process targetProcess, IntPtr address, params int[] offsets) where T : struct {
        if (targetProcess == null || address == IntPtr.Zero) {
            return default(T);
        }

        int last = OffsetAddress(targetProcess, ref address, offsets);
        if (address == IntPtr.Zero) {
            return default(T);
        }

        Type type = typeof(T);
        type = (type.IsEnum ? Enum.GetUnderlyingType(type) : type);

        int count = (type == typeof(bool)) ? 1 : Marshal.SizeOf(type);
        byte[] buffer = Read(targetProcess, address + last, count);

        object obj = ResolveToType(buffer, type);
        return (T) ((object) obj);
    }

    private static object ResolveToType(byte[] bytes, Type type) {
        if (type == typeof(int)) {
            return BitConverter.ToInt32(bytes, 0);
        } else if (type == typeof(uint)) {
            return BitConverter.ToUInt32(bytes, 0);
        } else if (type == typeof(float)) {
            return BitConverter.ToSingle(bytes, 0);
        } else if (type == typeof(double)) {
            return BitConverter.ToDouble(bytes, 0);
        } else if (type == typeof(byte)) {
            return bytes[0];
        } else if (type == typeof(sbyte)) {
            return (sbyte) bytes[0];
        } else if (type == typeof(bool)) {
            return bytes != null && bytes[0] > 0;
        } else if (type == typeof(short)) {
            return BitConverter.ToInt16(bytes, 0);
        } else if (type == typeof(ushort)) {
            return BitConverter.ToUInt16(bytes, 0);
        } else if (type == typeof(long)) {
            return BitConverter.ToInt64(bytes, 0);
        } else if (type == typeof(ulong)) {
            return BitConverter.ToUInt64(bytes, 0);
        } else {
            GCHandle gCHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try {
                return Marshal.PtrToStructure(gCHandle.AddrOfPinnedObject(), type);
            } finally {
                gCHandle.Free();
            }
        }
    }

    public static byte[] Read(this Process targetProcess, IntPtr address, int numBytes) {
        byte[] buffer = new byte[numBytes];
        if (targetProcess == null || address == IntPtr.Zero) {
            return buffer;
        }

        int bytesRead;
        WinAPI.ReadProcessMemory(targetProcess.Handle, address, buffer, numBytes, out bytesRead);
        return buffer;
    }

    public static byte[] Read(this Process targetProcess, IntPtr address, int numBytes, params int[] offsets) {
        byte[] buffer = new byte[numBytes];
        if (targetProcess == null || address == IntPtr.Zero) {
            return buffer;
        }

        int last = OffsetAddress(targetProcess, ref address, offsets);
        if (address == IntPtr.Zero) {
            return buffer;
        }

        int bytesRead;
        WinAPI.ReadProcessMemory(targetProcess.Handle, address + last, buffer, numBytes, out bytesRead);
        return buffer;
    }

    public static string Read(this Process targetProcess, IntPtr address) {
        if (targetProcess == null || address == IntPtr.Zero) {
            return string.Empty;
        }

        int length = Read<int>(targetProcess, address, 0x4);
        if (length < 0 || length > 2048) {
            return string.Empty;
        }

        return Encoding.Unicode.GetString(Read(targetProcess, address + 0x8, 2 * length));
    }

    public static string Read(this Process targetProcess, IntPtr address, params int[] offsets) {
        if (targetProcess == null || address == IntPtr.Zero) {
            return string.Empty;
        }

        int last = OffsetAddress(targetProcess, ref address, offsets);
        if (address == IntPtr.Zero) {
            return string.Empty;
        }

        int length = Read<int>(targetProcess, address + last, 0x4);
        if (length < 0 || length > 2048) {
            return string.Empty;
        }

        return Encoding.Unicode.GetString(Read(targetProcess, address + last + 0x8, 2 * length));
    }

    public static string ReadAscii(this Process targetProcess, IntPtr address) {
        if (targetProcess == null || address == IntPtr.Zero) {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder();
        byte[] data = new byte[128];
        int bytesRead;
        int offset = 0;
        bool invalid = false;
        do {
            WinAPI.ReadProcessMemory(targetProcess.Handle, address + offset, data, 128, out bytesRead);
            int i = 0;
            while (i < bytesRead) {
                byte d = data[i++];
                if (d == 0) {
                    i--;
                    break;
                } else if (d > 127) {
                    invalid = true;
                    break;
                }
            }

            if (i > 0) {
                sb.Append(Encoding.ASCII.GetString(data, 0, i));
            }

            if (i < bytesRead || invalid) {
                break;
            }

            offset += 128;
        } while (bytesRead > 0);

        return invalid ? string.Empty : sb.ToString();
    }

    public static void Write<T>(this Process targetProcess, IntPtr address, T value, params int[] offsets) where T : struct {
        if (targetProcess == null) {
            return;
        }

        int last = OffsetAddress(targetProcess, ref address, offsets);
        if (address == IntPtr.Zero) {
            return;
        }

        byte[] buffer = null;
        if (typeof(T) == typeof(bool)) {
            buffer = BitConverter.GetBytes(Convert.ToBoolean(value));
        } else if (typeof(T) == typeof(byte)) {
            buffer = BitConverter.GetBytes(Convert.ToByte(value));
        } else if (typeof(T) == typeof(sbyte)) {
            buffer = BitConverter.GetBytes(Convert.ToSByte(value));
        } else if (typeof(T) == typeof(int)) {
            buffer = BitConverter.GetBytes(Convert.ToInt32(value));
        } else if (typeof(T) == typeof(uint)) {
            buffer = BitConverter.GetBytes(Convert.ToUInt32(value));
        } else if (typeof(T) == typeof(short)) {
            buffer = BitConverter.GetBytes(Convert.ToInt16(value));
        } else if (typeof(T) == typeof(ushort)) {
            buffer = BitConverter.GetBytes(Convert.ToUInt16(value));
        } else if (typeof(T) == typeof(long)) {
            buffer = BitConverter.GetBytes(Convert.ToInt64(value));
        } else if (typeof(T) == typeof(ulong)) {
            buffer = BitConverter.GetBytes(Convert.ToUInt64(value));
        } else if (typeof(T) == typeof(float)) {
            buffer = BitConverter.GetBytes(Convert.ToSingle(value));
        } else if (typeof(T) == typeof(double)) {
            buffer = BitConverter.GetBytes(Convert.ToDouble(value));
        }

        int bytesWritten;
        WinAPI.WriteProcessMemory(targetProcess.Handle, address + last, buffer, buffer.Length, out bytesWritten);
    }

    public static void Write(this Process targetProcess, IntPtr address, byte[] value, params int[] offsets) {
        if (targetProcess == null) {
            return;
        }

        int last = OffsetAddress(targetProcess, ref address, offsets);
        if (address == IntPtr.Zero) {
            return;
        }

        int bytesWritten;
        WinAPI.WriteProcessMemory(targetProcess.Handle, address + last, value, value.Length, out bytesWritten);
    }

    private static int OffsetAddress(this Process targetProcess, ref IntPtr address, params int[] offsets) {
        byte[] buffer = new byte[is64Bit ? 8 : 4];
        int bytesRead;
        for (int i = 0; i < offsets.Length - 1; i++) {
            WinAPI.ReadProcessMemory(targetProcess.Handle, address + offsets[i], buffer, buffer.Length, out bytesRead);
            if (is64Bit) {
                address = (IntPtr) BitConverter.ToUInt64(buffer, 0);
            } else {
                address = (IntPtr) BitConverter.ToUInt32(buffer, 0);
            }

            if (address == IntPtr.Zero) {
                break;
            }
        }

        return offsets.Length > 0 ? offsets[offsets.Length - 1] : 0;
    }

    public static bool Is64Bit(this Process process) {
        if (process == null) {
            return false;
        }

        bool flag;
        WinAPI.IsWow64Process(process.Handle, out flag);
        return Environment.Is64BitOperatingSystem && !flag;
    }

    public static Module64 MainModule64(this Process p) {
        Module64[] modules = p.Modules64();
        return modules == null || modules.Length == 0 ? null : modules[0];
    }

    public static Module64[] Modules64(this Process p) {
        if (ModuleCache.Count > 100) {
            ModuleCache.Clear();
        }

        IntPtr[] buffer = new IntPtr[1024];
        uint cb = (uint) (IntPtr.Size * buffer.Length);
        uint totalModules;
        if (!WinAPI.EnumProcessModulesEx(p.Handle, buffer, cb, out totalModules, 3u)) {
            return null;
        }

        uint moduleSize = totalModules / (uint) IntPtr.Size;
        int key = p.StartTime.GetHashCode() + p.Id + (int) moduleSize;
        if (ModuleCache.ContainsKey(key)) {
            return ModuleCache[key];
        }

        List<Module64> list = new List<Module64>();
        StringBuilder stringBuilder = new StringBuilder(260);
        int count = 0;
        while ((long) count < (long) ((ulong) moduleSize)) {
            stringBuilder.Clear();
            if (WinAPI.GetModuleFileNameEx(p.Handle, buffer[count], stringBuilder, (uint) stringBuilder.Capacity) == 0u) {
                return list.ToArray();
            }

            string fileName = stringBuilder.ToString();
            stringBuilder.Clear();
            if (WinAPI.GetModuleBaseName(p.Handle, buffer[count], stringBuilder, (uint) stringBuilder.Capacity) == 0u) {
                return list.ToArray();
            }

            string moduleName = stringBuilder.ToString();
            ModuleInfo modInfo = default(ModuleInfo);
            if (!WinAPI.GetModuleInformation(p.Handle, buffer[count], out modInfo, (uint) Marshal.SizeOf(modInfo))) {
                return list.ToArray();
            }

            list.Add(new Module64 {
                FileName = fileName,
                BaseAddress = modInfo.BaseAddress,
                MemorySize = (int) modInfo.ModuleSize,
                EntryPointAddress = modInfo.EntryPoint,
                Name = moduleName
            });
            count++;
        }

        ModuleCache.Add(key, list.ToArray());
        return list.ToArray();
    }

    private static class WinAPI {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize,
            out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize,
            out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb, out uint lpcbNeeded,
            uint dwFilterFlag);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, uint nSize);

        [DllImport("psapi.dll")]
        public static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, uint nSize);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out ModuleInfo lpmodinfo, uint cb);
    }
}

public class Module64 {
    public IntPtr BaseAddress { get; set; }
    public IntPtr EntryPointAddress { get; set; }
    public string FileName { get; set; }
    public int MemorySize { get; set; }
    public string Name { get; set; }

    public FileVersionInfo FileVersionInfo {
        get { return FileVersionInfo.GetVersionInfo(FileName); }
    }

    public override string ToString() {
        return Name ?? base.ToString();
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct ModuleInfo {
    public IntPtr BaseAddress;
    public uint ModuleSize;
    public IntPtr EntryPoint;
}

[StructLayout(LayoutKind.Sequential)]
public struct MemInfo {
    public IntPtr BaseAddress;
    public IntPtr AllocationBase;
    public uint AllocationProtect;
    public IntPtr RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;

    public override string ToString() {
        return BaseAddress.ToString("X") + " " + Protect.ToString("X") + " " + State.ToString("X") + " " + Type.ToString("X") + " " +
               RegionSize.ToString("X");
    }
}

public class MemorySearcher {
    public Func<MemInfo, bool> MemoryFilter = delegate(MemInfo info) { return (info.State & 0x1000) != 0 && (info.Protect & 0x100) == 0; };

    public List<MemInfo> memoryInfo;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer, uint size, int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MemInfo lpBuffer, int dwLength);

    public byte[] ReadMemory(Process process, int index) {
        MemInfo info = memoryInfo[index];
        byte[] buff = new byte[(uint) info.RegionSize];
        ReadProcessMemory(process.Handle, info.BaseAddress, buff, (uint) info.RegionSize, 0);
        return buff;
    }

    public IntPtr FindSignature(Process process, string signature) {
        byte[] pattern;
        bool[] mask;
        GetSignature(signature, out pattern, out mask);
        GetMemoryInfo(process.Handle);
        int[] offsets = GetCharacterOffsets(pattern, mask);

        for (int i = 0; i < memoryInfo.Count; i++) {
            byte[] buff = ReadMemory(process, i);
            MemInfo info = memoryInfo[i];

            int result = ScanMemory(buff, pattern, mask, offsets);
            if (result != int.MinValue) {
                return info.BaseAddress + result;
            }
        }

        return IntPtr.Zero;
    }

    public List<IntPtr> FindSignatures(Process process, string signature) {
        byte[] pattern;
        bool[] mask;
        GetSignature(signature, out pattern, out mask);
        GetMemoryInfo(process.Handle);
        int[] offsets = GetCharacterOffsets(pattern, mask);

        List<IntPtr> pointers = new List<IntPtr>();
        for (int i = 0; i < memoryInfo.Count; i++) {
            byte[] buff = ReadMemory(process, i);
            MemInfo info = memoryInfo[i];

            ScanMemory(pointers, info, buff, pattern, mask, offsets);
        }

        return pointers;
    }

    public void GetMemoryInfo(IntPtr pHandle) {
        if (memoryInfo != null) {
            return;
        }

        memoryInfo = new List<MemInfo>();
        IntPtr current = (IntPtr) 65536;
        while (true) {
            MemInfo memInfo = new MemInfo();
            int dump = VirtualQueryEx(pHandle, current, out memInfo, Marshal.SizeOf(memInfo));
            if (dump == 0) {
                break;
            }

            long regionSize = (long) memInfo.RegionSize;
            if (regionSize <= 0 || (int) regionSize != regionSize) {
                if (MemoryReader.is64Bit) {
                    current = (IntPtr) ((ulong) memInfo.BaseAddress + (ulong) memInfo.RegionSize);
                    continue;
                }

                break;
            }

            if (MemoryFilter(memInfo)) {
                memoryInfo.Add(memInfo);
            }

            current = memInfo.BaseAddress + (int) regionSize;
        }
    }

    private int ScanMemory(byte[] data, byte[] search, bool[] mask, int[] offsets) {
        int current = 0;
        int end = search.Length - 1;
        while (current <= data.Length - search.Length) {
            for (int i = end; data[current + i] == search[i] || mask[i]; i--) {
                if (i == 0) {
                    return current;
                }
            }

            int offset = offsets[data[current + end]];
            current += offset;
        }

        return int.MinValue;
    }

    private void ScanMemory(List<IntPtr> pointers, MemInfo info, byte[] data, byte[] search, bool[] mask, int[] offsets) {
        int current = 0;
        int end = search.Length - 1;
        while (current <= data.Length - search.Length) {
            for (int i = end; data[current + i] == search[i] || mask[i]; i--) {
                if (i == 0) {
                    pointers.Add(info.BaseAddress + current);
                    break;
                }
            }

            int offset = offsets[data[current + end]];
            current += offset;
        }
    }

    private int[] GetCharacterOffsets(byte[] search, bool[] mask) {
        int[] offsets = new int[256];
        int unknown = 0;
        int end = search.Length - 1;
        for (int i = 0; i < end; i++) {
            if (!mask[i]) {
                offsets[search[i]] = end - i;
            } else {
                unknown = end - i;
            }
        }

        if (unknown == 0) {
            unknown = search.Length;
        }

        for (int i = 0; i < 256; i++) {
            int offset = offsets[i];
            if (unknown < offset || offset == 0) {
                offsets[i] = unknown;
            }
        }

        return offsets;
    }

    private void GetSignature(string searchString, out byte[] pattern, out bool[] mask) {
        int length = searchString.Length >> 1;
        pattern = new byte[length];
        mask = new bool[length];

        length <<= 1;
        for (int i = 0, j = 0; i < length; i++) {
            byte temp = (byte) (((int) searchString[i] - 0x30) & 0x1F);
            pattern[j] |= temp > 0x09 ? (byte) (temp - 7) : temp;
            if (searchString[i] == '?') {
                mask[j] = true;
                pattern[j] = 0;
            }

            if ((i & 1) == 1) {
                j++;
            } else {
                pattern[j] <<= 4;
            }
        }
    }
}
}