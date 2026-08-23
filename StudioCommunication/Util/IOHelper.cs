using System.Diagnostics;
using System.Runtime.InteropServices;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace StudioCommunication.Util;

public static class IOHelper {
    #region Process
    
    public static Process? OpenInDefaultApp(string path) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }});
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return Process.Start("open", $"'{path}'");
        } else {
            throw new NotImplementedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }

    /// Sends a termination signal to the process to gracefully exit
    public static void Terminate(this Process process) {
        // Unix
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Process.Start("kill", $"-s SIGINT {process.Id.ToString()}");
            return;
        }

        // Windows
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        const uint WM_CLOSE = 0x0010;
        
        if (process.MainWindowHandle != IntPtr.Zero) {
            PostMessage(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
    
    #endregion
    #region File
    
    public static void WriteToFileSafeOrThrow(string path, string content) {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        if (!WriteToFileSafe(path, stream)) {
            throw new IOException($"Could not safely write to file '{path}'");
        }
    }

    /// Writes the content to a temporary file and validates the written hash.
    /// Will atomically move the file into the destination after it has been validated.
    /// Returns `true` if the write was successful, otherwise `false`.
    public static bool WriteToFileSafe(string path, Stream content) {
        using var sha1 = SHA1.Create();
        byte[] contentHash = sha1.ComputeHash(content);
        content.Seek(0, SeekOrigin.Begin);
        
        string tmpPath = path + ".tmp";
        using (var tmpFs = File.Create(tmpPath)) {
            content.CopyTo(tmpFs);
            tmpFs.Flush();
        }

        byte[] fileHash;
        using (var tmpFs = File.OpenRead(tmpPath)) {
            fileHash = sha1.ComputeHash(tmpFs);
        }
        
        if (!contentHash.SequenceEqual(fileHash)) {
            File.Delete(tmpPath);
            return false;
        }
        
#if NETCOREAPP3_0_OR_GREATER
        File.Move(tmpPath, path, overwrite: true);
#else
        File.Copy(tmpPath, path, overwrite: true);
        File.Delete(tmpPath);
#endif

        return true;
    }
#if NET5_0_OR_GREATER
    public static async Task WriteToFileSafeOrThrowAsync(string path, string content) {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        if (!await WriteToFileSafeAsync(path, stream)) {
            throw new IOException($"Could not safely write to file '{path}'");
        }
    }
    
    /// Writes the content to a temporary file and validates the written hash.
    /// Will atomically move the file into the destination after it has been validated.
    /// Returns `true` if the write was successful, otherwise `false`.
    public static async Task<bool> WriteToFileSafeAsync(string path, Stream content) {
        using var sha1 = SHA1.Create();
        byte[] contentHash = await sha1.ComputeHashAsync(content);
        content.Seek(0, SeekOrigin.Begin);
        
        string tmpPath = path + ".tmp";
        await using (var tmpFs = File.OpenWrite(tmpPath)) {
            await content.CopyToAsync(tmpFs);
            await tmpFs.FlushAsync();
        }

        byte[] fileHash;
        await using (var tmpFs = File.OpenRead(tmpPath)) {
            fileHash = await sha1.ComputeHashAsync(tmpFs);
        }
        
        if (!contentHash.SequenceEqual(fileHash)) {
            File.Delete(tmpPath);
            return false;
        }
        
#if NETCOREAPP3_0_OR_GREATER
        File.Move(tmpPath, path, overwrite: true);
#else
        File.Copy(tmpPath, path, overwrite: true);
        File.Delete(tmpPath);
#endif
        
        return true;
    }
#endif
    
    #endregion
}
