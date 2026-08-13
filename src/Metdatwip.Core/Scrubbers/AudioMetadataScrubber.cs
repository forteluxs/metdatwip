using System.Text;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Scrubbers;

public sealed class AudioMetadataScrubber : IMetadataScrubber
{
    private static readonly string[] SupportedExtensions = [".mp3", ".wav"];
    private readonly AudioMetadataReader _reader;

    public AudioMetadataScrubber(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new AudioMetadataReader(sensitivityClassifier);
    }

    public string Name => "audio-metadata-scrubber";

    public bool CanScrub(string filePath, byte[]? magicBytes = null)
    {
        return _reader.CanRead(filePath, magicBytes);
    }

    public async Task<ScrubResult> ScrubAsync(
        string inputPath,
        string outputPath,
        ScrubProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(profile);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input audio file not found.", inputPath);
        }

        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
        var beforeDocument = await _reader.ReadAsync(inputPath, cancellationToken);

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        byte[] resultBytes;

        if (extension == ".mp3" || (inputBytes.Length >= 3 && inputBytes[0] == 0x49 && inputBytes[1] == 0x44 && inputBytes[2] == 0x33))
        {
            resultBytes = ScrubMp3(inputBytes);
        }
        else
        {
            resultBytes = ScrubWav(inputBytes);
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await File.WriteAllBytesAsync(outputPath, resultBytes, cancellationToken);

        var afterDocument = await _reader.ReadAsync(outputPath, cancellationToken);
        var removedCount = beforeDocument.Fields.Count - afterDocument.Fields.Count;
        var keptCount = afterDocument.Fields.Count;
        var sensitiveRemaining = afterDocument.Fields.Count(f => f.IsSensitive);

        return new ScrubResult(
            inputPath,
            outputPath,
            Math.Max(0, removedCount),
            keptCount,
            true,
            "Audio metadata scrubbed successfully.");
    }

    private static byte[] ScrubMp3(byte[] source)
    {
        var startOffset = 0;

        // Skip ID3v2 at beginning
        if (source.Length >= 10 && source[0] == 0x49 && source[1] == 0x44 && source[2] == 0x33)
        {
            var tagSize = ReadSynchsafeInt(source, 6);
            startOffset = 10 + tagSize;
            if (startOffset > source.Length) startOffset = 0;
        }

        var endOffset = source.Length;

        // Strip ID3v1 at end
        if (endOffset - startOffset >= 128)
        {
            var v1Pos = endOffset - 128;
            if (source[v1Pos] == 'T' && source[v1Pos + 1] == 'A' && source[v1Pos + 2] == 'G')
            {
                endOffset -= 128;
            }
        }

        var length = endOffset - startOffset;
        var result = new byte[length];
        Array.Copy(source, startOffset, result, 0, length);
        return result;
    }

    private static byte[] ScrubWav(byte[] source)
    {
        if (source.Length < 12) return source;

        using var output = new MemoryStream(source.Length);
        output.Write(source, 0, 12); // Copy RIFF header + WAVE

        var pos = 12;
        while (pos + 8 <= source.Length)
        {
            var chunkId = Encoding.ASCII.GetString(source, pos, 4);
            var chunkSize = BitConverter.ToInt32(source, pos + 4);
            if (chunkSize < 0 || pos + 8 + chunkSize > source.Length) break;

            var chunkTotal = 8 + chunkSize + (chunkSize % 2);

            // Skip LIST INFO and ID3 chunks
            var isListInfo = chunkId == "LIST" && chunkSize >= 4 && Encoding.ASCII.GetString(source, pos + 8, 4) == "INFO";
            var isId3 = chunkId.Equals("id3 ", StringComparison.OrdinalIgnoreCase) || chunkId.Equals("ID3 ", StringComparison.OrdinalIgnoreCase);

            if (!isListInfo && !isId3)
            {
                output.Write(source, pos, Math.Min(chunkTotal, source.Length - pos));
            }

            pos += chunkTotal;
        }

        var bytes = output.ToArray();
        // Update RIFF payload size at offset 4
        var payloadSize = bytes.Length - 8;
        BitConverter.GetBytes(payloadSize).CopyTo(bytes, 4);
        return bytes;
    }

    private static int ReadSynchsafeInt(byte[] bytes, int offset)
    {
        return (bytes[offset] & 0x7F) << 21 |
               (bytes[offset + 1] & 0x7F) << 14 |
               (bytes[offset + 2] & 0x7F) << 7 |
               (bytes[offset + 3] & 0x7F);
    }
}
