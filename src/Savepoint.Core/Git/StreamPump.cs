using System.Buffers;
using System.Text;

namespace Savepoint.Core.Git;

internal static class StreamPump
{
    // Room for the bytes the decoder holds over from the previous read.
    private const int _decoderSlack = 4;

    // One decoder for the whole stream, so a character split across two reads still comes out whole.
    public static async Task ReadTextAsync(Stream stream, int bufferSize, Action<ReadOnlySpan<char>> onText, CancellationToken ct)
    {
        var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();
        var bytes = ArrayPool<byte>.Shared.Rent(bufferSize);
        var chars = ArrayPool<char>.Shared.Rent(bufferSize + _decoderSlack);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(bytes.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                if (count > 0)
                {
                    onText(chars.AsSpan(0, count));
                }
            }

            var tail = decoder.GetChars([], 0, 0, chars, 0, flush: true);
            if (tail > 0)
            {
                onText(chars.AsSpan(0, tail));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    public static async Task ReadBytesAsync(Stream stream, int bufferSize, Action<ReadOnlySpan<byte>> onBytes, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
            {
                onBytes(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
