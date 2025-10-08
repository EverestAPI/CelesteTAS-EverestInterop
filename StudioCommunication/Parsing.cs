using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StudioCommunication;

/// TAS-file parsing logic, shared between CelesteTAS and Studio
public static class Parsing {
    /// Searches for the correct target file for a Read command
    public static string? FindReadTargetFile(string fileDirectory, string filePath, out string errorMessage) {
        string path = Path.Combine(fileDirectory, filePath);
        if (!path.EndsWith(".tas", StringComparison.InvariantCulture)) {
            path += ".tas";
        }

        if (File.Exists(path)) {
            errorMessage = string.Empty;
            return path;
        }

        // Windows allows case-insensitive names, but Linux/macOS don't...
        string[] components = filePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0) {
            errorMessage = "No file path specified";
            return null;
        }

        string realDirectory = fileDirectory;
        for (int i = 0; i < components.Length - 1; i++) {
            string directory = components[i];

            if (directory == "..") {
                string? parentDirectory = Path.GetDirectoryName(realDirectory);
                if (parentDirectory == null) {
                    errorMessage = $"Parent directory for '{realDirectory}' not found";
                    return null;
                }

                realDirectory = parentDirectory;
                continue;
            }

            string[] directories = Directory.EnumerateDirectories(realDirectory)
                .Where(d => Path.GetFileName(d).Equals(directory, StringComparison.InvariantCultureIgnoreCase))
                .ToArray();

            if (directories.Length > 1) {
                errorMessage = $"Ambiguous match for directory '{directory}'";
                return null;
            }
            if (directories.Length == 0) {
                errorMessage = $"Couldn't find directory '{directory}'";
                return null;
            }

            realDirectory = Path.Combine(realDirectory, directories[0]);
        }

        string file = Path.GetFileNameWithoutExtension(components[^1]);
        string[] files = Directory.EnumerateFiles(realDirectory)
            // Allow an optional suffix on file names. Example: 9D_04 -> 9D_04_Curiosity.tas
            .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(file, StringComparison.InvariantCultureIgnoreCase))
            .ToArray();

        if (files.Length > 1) {
            errorMessage = $"Ambiguous match for file '{file}'";
            return null;
        }
        if (files.Length == 1) {
            path = Path.Combine(realDirectory, files[0]);
            if (File.Exists(path)) {
                errorMessage = string.Empty;
                return path;
            }
        }

        errorMessage = $"Couldn't find file '{file}'";
        return null;
    }

    /// Searches for the line number (1-indexed) of the target label in the file
    public static bool TryGetLineTarget(string labelOrLineNumber, string[] lines, out int lineNumber, out bool isLabel) {
        if (int.TryParse(labelOrLineNumber, out lineNumber)) {
            isLabel = false;
            return true;
        }

        var labelRegex = new Regex(@$"^#\s*{Regex.Escape(labelOrLineNumber)}$");
        for (lineNumber = 1; lineNumber <= lines.Length; lineNumber++) {
#if NET7_0_OR_GREATER
            if (labelRegex.IsMatch(lines[lineNumber - 1].AsSpan().Trim())) {
#else
            if (labelRegex.IsMatch(lines[lineNumber - 1].Trim())) {
#endif
                isLabel = true;
                return true;
            }
        }

        isLabel = false;
        return false;
    }
}
