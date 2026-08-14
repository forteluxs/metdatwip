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

        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        var isSameFile = string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        var targetFile = isSameFile ? Path.Combine(Path.GetTempPath(), "metdatwip_scrub_vid_" + Guid.NewGuid().ToString("N") + Path.GetExtension(inputPath)) : outputFullPath;

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

        var outDir = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        await File.WriteAllBytesAsync(targetFile, resultBytes, cancellationToken);

        if (isSameFile)
        {
            File.Move(targetFile, outputFullPath, overwrite: true);
        }

        var afterDocument = await _reader.ReadAsync(outputFullPath, cancellationToken);
        var removedCount = beforeDocument.Fields.Count - afterDocument.Fields.Count;
        var keptCount = afterDocument.Fields.Count;

        return new ScrubResult(
            inputPath,
            outputFullPath,
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
                pos += boxSize;
                continue;
            }

            if (boxType == "moov")
            {
                using var innerMoov = new MemoryStream();
                var mpos = pos + 8;
                var mend = pos + boxSize;

                while (mpos + 8 <= mend)
                {
                    var msize = ReadInt32BigEndian(source, mpos);
                    var mbtype = Encoding.ASCII.GetString(source, mpos + 4, 4);
                    if (msize <= 0 || mpos + msize > mend) break;

                    if (mbtype != "udta")
                    {
                        innerMoov.Write(source, mpos, msize);
                    }

                    mpos += msize;
                }

                var moovBytes = innerMoov.ToArray();
                var newMoovHeader = new byte[8];
                WriteInt32BigEndian(newMoovHeader, 0, 8 + moovBytes.Length);
                Encoding.ASCII.GetBytes("moov").CopyTo(newMoovHeader, 4);
                output.Write(newMoovHeader, 0, 8);
                output.Write(moovBytes, 0, moovBytes.Length);

                pos += boxSize;
                continue;
            }

            output.Write(source, pos, boxSize);
            pos += boxSize;
        }

        return output.ToArray();
    }

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((value >> 24) & 0xFF);
        bytes[offset + 1] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 3] = (byte)(value & 0xFF);
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}
