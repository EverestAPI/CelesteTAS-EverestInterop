using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CelesteStudio.Entities {
//.load C:\Windows\Microsoft.NET\Framework\v4.0.30319\SOS.dll
public class GameMemory {
    private static readonly ProgramPointer TAS = new ProgramPointer(AutoDeref.Single,
        new ProgramSignature(PointerVersion.XNA, "8B0D????????3909FF15????????EB158325", 2),
        new ProgramSignature(PointerVersion.OpenGL, "007417908B0D????????3909", 6));

    private static readonly ProgramPointer Celeste = new ProgramPointer(AutoDeref.Single,
        new ProgramSignature(PointerVersion.XNA, "83C604F30F7E06660FD6078BCBFF15????????8D15", 21),
        new ProgramSignature(PointerVersion.OpenGL, "8B55F08B45E88D5274E8????????8B45F08D15", 19),
        new ProgramSignature(PointerVersion.Itch, "8D5674E8????????8D15????????E8????????C605", 10));

    private DateTime lastHooked;
    private string output;
    private string playeroutput;
    private string room;

    public GameMemory() {
        lastHooked = DateTime.MinValue;
    }

    public Process Program { get; set; }
    public bool IsHooked { get; set; } = false;

    public string TASOutput() {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return output;
        } else {
            return TAS.Read(Program, 0x4, 0x0);
        }
    }

    public string TASPlayerOutput() {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return playeroutput;
        } else {
            return TAS.Read(Program, 0x8, 0x0);
        }
    }

    public string LevelName() {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            return room;
        } else {
            //Celeste.Instance.AutosplitterInfo.Level
            if (Celeste.Version == PointerVersion.XNA) {
                return Celeste.Read(Program, 0x0, 0xac, 0x14, 0x0);
            }

            return Celeste.Read(Program, 0x0, 0x8c, 0x14, 0x0);
        }
    }

    private void ReadStreamAsync() {
        while (true) {
            var task = Task.Run(() => ReadStream());
            task.Wait(TimeSpan.FromMilliseconds(500));
        }
    }

    private void ReadStream() {
        string line = null;
        StreamReader UnixRTCStream = new StreamReader("/tmp/celestetas");
        while (UnixRTCStream.Peek() > 0) {
            line = UnixRTCStream.ReadLine();
        }

        if (line != null) {
            string[] lines = line.Split('%');
            lines = lines.Select((x) => x.Replace('~', '\n')).ToArray();
            if (lines.Length >= 3) {
                playeroutput = lines[0];
                output = lines[1];
                room = lines[2];
            }
        }

        UnixRTCStream.Dispose();
    }

    public bool HookProcess() {
        if (Environment.OSVersion.Platform == PlatformID.Unix) {
            if (!IsHooked) {
                try {
                    StreamReader UnixRTCStream = new StreamReader("/tmp/celestetas");
                    while (UnixRTCStream.Peek() > 0) {
                        UnixRTCStream.Read();
                    }

                    UnixRTCStream.Dispose();
                    Task.Run(() => ReadStreamAsync());
                    playeroutput = "";
                    output = "";
                    room = "";
                    IsHooked = true;
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            }
        } else {
            IsHooked = Program != null && !Program.HasExited;
            if (!IsHooked && DateTime.Now > lastHooked.AddSeconds(1)) {
                lastHooked = DateTime.Now;
                Process[] processes = Process.GetProcessesByName("Celeste");
                Program = processes != null && processes.Length > 0 ? processes[0] : null;

                if (Program != null && !Program.HasExited) {
                    MemoryReader.Update64Bit(Program);
                    IsHooked = true;
                }
            }
        }

        return IsHooked;
    }

    public void Dispose() {
        if (Program != null) {
            Program.Dispose();
        }
    }
}
}