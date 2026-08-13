using System.Text;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;

namespace Metdatwip.Core.Readers;

public sealed class VideoMetadataReader : IMetadataReader
{
    private static readonly string[] SupportedExtensions = [".mp4", ".mov", ".m4v", ".mkv", ".webm"];
    private readonly ISensitivityClassifier _sensitivityClassifier;

    public VideoMetadataReader(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _sensitivityClassifier = sensitivityClassifier;
    }

    public string Name => "video-metadata-reader";

    public bool CanRead(string filePath, byte[]? magicBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension.ToLowerInvariant()))
        {
            return true;
        }

        if (magicBytes is not null && magicBytes.Length >= 8)
        {
            // MP4 ftyp box or MKV EBML magic 1A 45 DF A3
            if (magicBytes[4] == 'f' && magicBytes[5] == 't' && magicBytes[6] == 'y' && magicBytes[7] == 'p') return true;
            if (magicBytes[0] == 0x1A && magicBytes[1] == 0x45 && magicBytes[2] == 0xDF && magicBytes[3] == 0xA3) return true;
        }

        return false;
    }

    public async Task<MetadataDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Video file not found.", filePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var document = new MetadataDocument(filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension is ".mp4" or ".mov" or ".m4v" || (bytes.Length >= 8 && bytes[4] == 'f' && bytes[5] == 't' && bytes[6] == 'y' && bytes[7] == 'p'))
        {
            ReadMp4Atoms(bytes, document);
        }
        else if (extension is ".mkv" or ".webm" || (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3))
        {
            ReadMkvTags(bytes, document);
        }

        return document;
    }

    private void ReadMp4Atoms(byte[] bytes, MetadataDocument document)
    {
        var pos = 0;
        while (pos + 8 <= bytes.Length)
        {
            var boxSize = ReadInt32BigEndian(bytes, pos);
            var boxType = Encoding.ASCII.GetString(bytes, pos + 4, 4);

            if (boxSize == 1 && pos + 16 <= bytes.Length) // 64-bit size
            {
                var size64 = ReadInt64BigEndian(bytes, pos + 8);
                if (size64 <= 0 || pos + size64 > bytes.Length) break;
                boxSize = (int)size64;
            }
            else if (boxSize <= 0 || pos + boxSize > bytes.Length) break;

            if (boxType == "moov")
            {
                ReadMp4Moov(bytes, pos + 8, pos + boxSize, document);
            }

            pos += boxSize;
        }
    }

    private void ReadMp4Moov(byte[] bytes, int start, int end, MetadataDocument document)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var boxSize = ReadInt32BigEndian(bytes, pos);
            var boxType = Encoding.ASCII.GetString(bytes, pos + 4, 4);
            if (boxSize <= 0 || pos + boxSize > end) break;

            if (boxType is "udta" or "meta")
            {
                var payloadStart = boxType == "meta" ? pos + 12 : pos + 8; // meta has 4 bytes flags/version
                ReadMp4IlstContainer(bytes, payloadStart, pos + boxSize, document);
            }

            pos += boxSize;
        }
    }

    private void ReadMp4IlstContainer(byte[] bytes, int start, int end, MetadataDocument document)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var boxSize = ReadInt32BigEndian(bytes, pos);
            var boxType = Encoding.ASCII.GetString(bytes, pos + 4, 4);
            if (boxSize <= 0 || pos + boxSize > end) break;

            if (boxType == "ilst")
            {
                ReadMp4IlstItems(bytes, pos + 8, pos + boxSize, document);
            }
            else if (boxType == "meta")
            {
                ReadMp4IlstContainer(bytes, pos + 12, pos + boxSize, document);
            }
            else if (boxType == "udta")
            {
                ReadMp4IlstContainer(bytes, pos + 8, pos + boxSize, document);
            }

            pos += boxSize;
        }
    }

    private void ReadMp4IlstItems(byte[] bytes, int start, int end, MetadataDocument document)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var itemSize = ReadInt32BigEndian(bytes, pos);
            var itemType = Encoding.Latin1.GetString(bytes, pos + 4, 4);
            if (itemSize <= 0 || pos + itemSize > end) break;

            // Look inside for data box
            var dataPos = pos + 8;
            while (dataPos + 8 <= pos + itemSize)
            {
                var dataSize = ReadInt32BigEndian(bytes, dataPos);
                var dataType = Encoding.ASCII.GetString(bytes, dataPos + 4, 4);
                if (dataSize <= 0 || dataPos + dataSize > pos + itemSize) break;

                if (dataType == "data" && dataSize >= 16)
                {
                    var text = Encoding.UTF8.GetString(bytes, dataPos + 16, dataSize - 16).TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var name = MapMp4AtomName(itemType);
                        AddField(document, "MP4-Metadata", name, text);
                    }
                }

                dataPos += dataSize;
            }

            pos += itemSize;
        }
    }

    private void ReadMkvTags(byte[] bytes, MetadataDocument document)
    {
        // Simple search for UTF-8 metadata strings within MKV EBML structure
        var str = Encoding.UTF8.GetString(bytes);
        var keywords = new[] { "TITLE=", "ARTIST=", "DATE_RELEASED=", "GENRE=", "ENCODER=", "SOFTWARE=", "COPYRIGHT=" };

        foreach (var kw in keywords)
        {
            var idx = str.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var endIdx = str.IndexOf('\0', idx);
                if (endIdx < 0) endIdx = str.IndexOf('\n', idx);
                if (endIdx > idx)
                {
                    var line = str.Substring(idx, endIdx - idx).Trim();
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        AddField(document, "MKV-Tags", parts[0], parts[1]);
                    }
                }
            }
        }
    }

    private void AddField(MetadataDocument document, string group, string name, string value)
    {
        var tentative = new MetadataField(group, name, value, false, true);
        var isSensitive = _sensitivityClassifier.IsSensitive(tentative);
        document.AddField(tentative with { IsSensitive = isSensitive });
    }

    private static string MapMp4AtomName(string atom)
    {
        var lower = atom.ToLowerInvariant();
        if (lower.Contains("nam")) return "Title";
        if (lower.Contains("art") || lower.Contains("rt")) return "Artist";
        if (lower.Contains("alb") || lower.Contains("lb")) return "Album";
        if (lower.Contains("cpy") || lower.Contains("py")) return "Copyright";
        if (lower.Contains("day") || lower.Contains("yr") || lower.EndsWith("y")) return "Year";
        if (lower.Contains("swr") || lower.Contains("tool")) return "Software";
        if (lower.Contains("cmt") || lower.Contains("mt")) return "Comment";
        return atom;
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static long ReadInt64BigEndian(byte[] bytes, int offset)
    {
        return ((long)bytes[offset] << 56) | ((long)bytes[offset + 1] << 48) |
               ((long)bytes[offset + 2] << 40) | ((long)bytes[offset + 3] << 32) |
               ((long)bytes[offset + 4] << 24) | ((long)bytes[offset + 5] << 16) |
               ((long)bytes[offset + 6] << 8) | bytes[offset + 7];
    }
}
