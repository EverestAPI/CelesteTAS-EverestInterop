using StudioCommunication;
using System.Collections.Generic;

namespace TAS.Input;

/// Describes additional information about a command, for Studio to use
public interface ITasCommandMeta {
    public string Description { get; }
    public string Insert { get; }
    public bool HasArguments { get; }

    public IAsyncEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args);
}
