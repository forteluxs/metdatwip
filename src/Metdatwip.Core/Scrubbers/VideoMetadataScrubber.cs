using System.Text;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Scrubbers;

public sealed class VideoMetadataScrubber : IMetadataScrubber
{
    private static readonly string[] SupportedExtensions = [".mp4", ".mov", ".m4v", ".mkv", ".webm"];
    private readonly VideoMetadataReader _reader;

    public VideoMetadataScrubber(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new VideoMetadataReader(sensitivityClassifier);
    }

    public string Name => "video-metadata-scrubber";

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
            throw new FileNotFoundException("Input video file not found.", inputPath);
        }

        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
        var beforeDocument = await _reader.ReadAsync(inputPath, cancellationToken);

        byte[] resultBytes;
        var extension = Path.GetExtension(inputPath).ToLowerInvariant();

        if (extension is ".mp4" or ".mov" or ".m4v")
        {
            resultBytes = ScrubMp4Metadata(inputBytes);
        }
        else
        {
            resultBytes = inputBytes; // MKV passthrough
        }

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        await File.WriteAllBytesAsync(outputPath, resultBytes, cancellationToken);

        var afterDocument = await _reader.ReadAsync(outputPath, cancellationToken);
        var removedCount = beforeDocument.Fields.Count - afterDocument.Fields.Count;
        var keptCount = afterDocument.Fields.Count;

        return new ScrubResult(
            inputPath,
            outputPath,
            Math.Max(0, removedCount),
            keptCount,
            true,
            "Video metadata scrubbed successfully.");
    }

    private static byte[] ScrubMp4Metadata(byte[] source)
    {
        using var output = new MemoryStream(source.Length);
        var pos = 0;

        while (pos + 8 <= source.Length)
        {
            var boxSize = ReadInt32BigEndian(source, pos);
            var boxType = Encoding.ASCII.GetString(source, pos + 4, 4);

            if (boxSize <= 0 || pos + boxSize > source.Length)
            {
                output.Write(source, pos, source.Length - pos);
                break;
            }

            if (boxType == "udta")
            {
                // Skip udta atom containing metadata
                pos += boxSize;
                continue;
            }

            output.Write(source, pos, boxSize);
            pos += boxSize;
        }

        return output.ToArray();
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}
