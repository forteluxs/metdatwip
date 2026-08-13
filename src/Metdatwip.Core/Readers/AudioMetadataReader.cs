using System.Text;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;

namespace Metdatwip.Core.Readers;

public sealed class AudioMetadataReader : IMetadataReader
{
    private static readonly string[] SupportedExtensions = [".mp3", ".wav"];
    private readonly ISensitivityClassifier _sensitivityClassifier;

    public AudioMetadataReader(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _sensitivityClassifier = sensitivityClassifier;
    }

    public string Name => "audio-metadata-reader";

    public bool CanRead(string filePath, byte[]? magicBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension.ToLowerInvariant()))
        {
            return true;
        }

        if (magicBytes is not null && magicBytes.Length >= 4)
        {
            // MP3 ID3v2 magic "ID3" or WAV "RIFF"
            if (magicBytes[0] == 0x49 && magicBytes[1] == 0x44 && magicBytes[2] == 0x33) return true;
            if (magicBytes[0] == 0x52 && magicBytes[1] == 0x49 && magicBytes[2] == 0x46 && magicBytes[3] == 0x46) return true;
        }

        return false;
    }

    public async Task<MetadataDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Audio file not found.", filePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var document = new MetadataDocument(filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ".mp3" || (bytes.Length >= 3 && bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33))
        {
            ReadId3v2Tags(bytes, document);
            ReadId3v1Tags(bytes, document);
        }
        else if (extension == ".wav" || (bytes.Length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46))
        {
            ReadWavInfoTags(bytes, document);
            ReadWavId3Chunk(bytes, document);
        }

        return document;
    }

    private void ReadId3v2Tags(byte[] bytes, MetadataDocument document)
    {
        if (bytes.Length < 10 || bytes[0] != 0x49 || bytes[1] != 0x44 || bytes[2] != 0x33) return;

        var majorVersion = bytes[3];
        var tagSize = ReadSynchsafeInt(bytes, 6);
        if (tagSize <= 0 || 10 + tagSize > bytes.Length) return;

        var pos = 10;
        var end = 10 + tagSize;

        while (pos + 10 <= end)
        {
            var frameId = Encoding.ASCII.GetString(bytes, pos, 4);
            if (string.IsNullOrWhiteSpace(frameId) || frameId[0] == '\0') break;

            int frameSize = majorVersion == 4
                ? ReadSynchsafeInt(bytes, pos + 4)
                : ((bytes[pos + 4] << 24) | (bytes[pos + 5] << 16) | (bytes[pos + 6] << 8) | bytes[pos + 7]);

            if (frameSize <= 0 || pos + 10 + frameSize > end) break;

            var frameDataOffset = pos + 10;
            var tagValue = DecodeFrameValue(bytes, frameDataOffset, frameSize);

            if (!string.IsNullOrWhiteSpace(tagValue))
            {
                var tagName = MapId3FrameName(frameId);
                AddField(document, "ID3v2", tagName, tagValue);
            }

            pos += 10 + frameSize;
        }
    }

    private void ReadId3v1Tags(byte[] bytes, MetadataDocument document)
    {
        if (bytes.Length < 128) return;
        var offset = bytes.Length - 128;
        if (bytes[offset] != 'T' || bytes[offset + 1] != 'A' || bytes[offset + 2] != 'G') return;

        var title = Encoding.Latin1.GetString(bytes, offset + 3, 30).TrimEnd('\0', ' ');
        var artist = Encoding.Latin1.GetString(bytes, offset + 33, 30).TrimEnd('\0', ' ');
        var album = Encoding.Latin1.GetString(bytes, offset + 63, 30).TrimEnd('\0', ' ');
        var year = Encoding.Latin1.GetString(bytes, offset + 93, 4).TrimEnd('\0', ' ');

        if (!string.IsNullOrWhiteSpace(title)) AddField(document, "ID3v1", "Title", title);
        if (!string.IsNullOrWhiteSpace(artist)) AddField(document, "ID3v1", "Artist", artist);
        if (!string.IsNullOrWhiteSpace(album)) AddField(document, "ID3v1", "Album", album);
        if (!string.IsNullOrWhiteSpace(year)) AddField(document, "ID3v1", "Year", year);
    }

    private void ReadWavInfoTags(byte[] bytes, MetadataDocument document)
    {
        if (bytes.Length < 12) return;
        var pos = 12; // Skip RIFF header (4) + length (4) + WAVE (4)

        while (pos + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, pos, 4);
            var chunkSize = BitConverter.ToInt32(bytes, pos + 4);
            if (chunkSize < 0 || pos + 8 + chunkSize > bytes.Length) break;

            if (chunkId == "LIST" && chunkSize >= 4)
            {
                var listType = Encoding.ASCII.GetString(bytes, pos + 8, 4);
                if (listType == "INFO")
                {
                    ReadListInfoSubChunks(bytes, pos + 12, pos + 8 + chunkSize, document);
                }
            }

            pos += 8 + chunkSize + (chunkSize % 2); // Word alignment
        }
    }

    private void ReadListInfoSubChunks(byte[] bytes, int start, int end, MetadataDocument document)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var subId = Encoding.ASCII.GetString(bytes, pos, 4);
            var subSize = BitConverter.ToInt32(bytes, pos + 4);
            if (subSize <= 0 || pos + 8 + subSize > end) break;

            var val = Encoding.UTF8.GetString(bytes, pos + 8, subSize).TrimEnd('\0', ' ');
            if (!string.IsNullOrWhiteSpace(val))
            {
                var name = MapInfoChunkName(subId);
                AddField(document, "RIFF-INFO", name, val);
            }

            pos += 8 + subSize + (subSize % 2);
        }
    }

    private void ReadWavId3Chunk(byte[] bytes, MetadataDocument document)
    {
        if (bytes.Length < 12) return;
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, pos, 4);
            var chunkSize = BitConverter.ToInt32(bytes, pos + 4);
            if (chunkSize < 0 || pos + 8 + chunkSize > bytes.Length) break;

            if (chunkId.Equals("id3 ", StringComparison.OrdinalIgnoreCase) || chunkId.Equals("ID3 ", StringComparison.OrdinalIgnoreCase))
            {
                var id3Bytes = new byte[chunkSize];
                Array.Copy(bytes, pos + 8, id3Bytes, 0, chunkSize);
                ReadId3v2Tags(id3Bytes, document);
            }

            pos += 8 + chunkSize + (chunkSize % 2);
        }
    }

    private void AddField(MetadataDocument document, string group, string name, string value)
    {
        var tentative = new MetadataField(group, name, value, false, true);
        var isSensitive = _sensitivityClassifier.IsSensitive(tentative);
        document.AddField(tentative with { IsSensitive = isSensitive });
    }

    private static string MapId3FrameName(string frameId) => frameId switch
    {
        "TIT2" => "Title",
        "TPE1" => "Artist",
        "TALB" => "Album",
        "TYER" or "TDRC" => "Year",
        "TCON" => "Genre",
        "TRCK" => "Track Number",
        "COMM" => "Comment",
        "TENC" or "TSSE" => "Software",
        "TCOP" => "Copyright",
        _ => frameId
    };

    private static string MapInfoChunkName(string chunkId) => chunkId switch
    {
        "INAM" => "Title",
        "IART" => "Artist",
        "IPRD" => "Album",
        "ICRD" => "Year",
        "IGNR" => "Genre",
        "ICMT" => "Comment",
        "ISFT" => "Software",
        "ICOP" => "Copyright",
        _ => chunkId
    };

    private static string DecodeFrameValue(byte[] bytes, int offset, int size)
    {
        if (size <= 1) return string.Empty;
        var encodingByte = bytes[offset];
        var textOffset = offset + 1;
        var textSize = size - 1;

        Encoding enc = encodingByte switch
        {
            1 => Encoding.Unicode,
            2 => Encoding.BigEndianUnicode,
            3 => Encoding.UTF8,
            _ => Encoding.Latin1
        };

        return enc.GetString(bytes, textOffset, textSize).TrimEnd('\0', ' ');
    }

    private static int ReadSynchsafeInt(byte[] bytes, int offset)
    {
        return (bytes[offset] & 0x7F) << 21 |
               (bytes[offset + 1] & 0x7F) << 14 |
               (bytes[offset + 2] & 0x7F) << 7 |
               (bytes[offset + 3] & 0x7F);
    }
}
