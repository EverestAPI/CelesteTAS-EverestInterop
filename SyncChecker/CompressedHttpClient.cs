using System.Net;
using System.Net.Sockets;

namespace SyncChecker;

/// <summary>
/// An HttpClient that supports compressed responses to save bandwidth, and uses IPv4 to work around issues for some users.
/// Taken from https://github.com/EverestAPI/Everest/blob/dev/Celeste.Mod.mm/Mod/Helpers/CompressedHttpClient.cs
/// </summary>
public class CompressedHttpClient : HttpClient {
    private static readonly SocketsHttpHandler handler = new() {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async delegate (SocketsHttpConnectionContext ctx, CancellationToken token) {
            if (ctx.DnsEndPoint.AddressFamily != AddressFamily.Unspecified && ctx.DnsEndPoint.AddressFamily != AddressFamily.InterNetwork) {
                throw new InvalidOperationException("no IPv4 address");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try {
                await socket.ConnectAsync(new DnsEndPoint(ctx.DnsEndPoint.Host, ctx.DnsEndPoint.Port, AddressFamily.InterNetwork), token).ConfigureAwait(false);
                return new NetworkStream(socket, true);
            } catch (Exception) {
                socket.Dispose();
                throw;
            }
        }
    };

    public CompressedHttpClient() : base(handler, disposeHandler: false) {
        DefaultRequestHeaders.Add("User-Agent", "CelesteTAS/SyncCheck");
    }
}
