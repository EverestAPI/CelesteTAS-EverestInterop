using StudioCommunication;

namespace TAS.Input.Commands;

public static class ExitGameCommand {
    [TasCommand("ExitGame")]
    private static void ExitGame(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        SDL2.SDL.SDL_Event ev = new() {
            type = SDL2.SDL.SDL_EventType.SDL_QUIT
        };
#pragma warning disable CA1806
        SDL2.SDL.SDL_PushEvent(ref ev);
#pragma warning restore CA1806
    }
}
