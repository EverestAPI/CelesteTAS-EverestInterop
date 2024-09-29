using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TAS.Input;

/// Describes additional information about a command, for Studio to use
public interface ITasCommandMeta {
    public string Insert { get; }
    public bool HasArguments { get; }

    /// Produces a hash for the specified arguments, to cache arguments
    public int GetHash(string[] args, string filePath, int fileLine) {
        // Exclude the last argument, since we're currently editing that
        return args[..Math.Max(0, args.Length - 1)]
            .Aggregate(17, (current, arg) => 31 * current + 17 * arg.GetStableHashCode());
    }

    /// Incrementally yields entries for auto-completion with the current arguments
    public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
        yield break;
    }
}
