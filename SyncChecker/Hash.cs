using System.Security.Cryptography;

namespace SyncChecker;

/// Combined hash from multiple sources
public readonly struct Hash(HashAlgorithm algorithm, int bufferSize = 8192) : IDisposable {
    private readonly byte[] buffer = new byte[bufferSize];

    public void Add(byte[] input) {
        int inputOffset = 0;
        while (inputOffset < input.Length) {
            algorithm.TransformBlock(input, inputOffset, Math.Min(buffer.Length, input.Length - inputOffset), buffer, 0);
            inputOffset += buffer.Length;
        }
    }

    public void Add(Stream stream) {
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
            algorithm.TransformBlock(buffer, 0, read, buffer, 0);
        }
    }
    public async Task AddAsync(Stream stream, CancellationToken cancellationToken = default) {
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
            algorithm.TransformBlock(buffer, 0, read, buffer, 0);
        }
    }

    public byte[] Compute() {
        algorithm.TransformFinalBlock(buffer, 0, 0);
        return algorithm.Hash!;
    }

    public void Dispose() {
        algorithm.Dispose();
    }
}
