using System.Text;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Scrubbers;

namespace Metdatwip.Core.Writers;

public sealed class AudioMetadataWriter : IMetadataWriter
{
    private static readonly string[] SupportedExtensions = [".mp3", ".wav"];
    private readonly AudioMetadataReader _reader;
    private readonly AudioMetadataScrubber _scrubber;

    public AudioMetadataWriter(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new AudioMetadataReader(sensitivityClassifier);
        _scrubber = new AudioMetadataScrubber(sensitivityClassifier);
    }

    public string Name => "audio-metadata-writer";

    public bool CanWrite(string filePath, byte[]? magicBytes = null)
    {
        return _reader.CanRead(filePath, magicBytes);
    }

    public async Task<WriteResult> WriteAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<MetadataEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(edits);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Audio file not found.", inputPath);
        }

        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        var isSameFile = string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        var targetFile = isSameFile
            ? Path.Combine(Path.GetTempPath(), "metdatwip_audio_tmp_" + Guid.NewGuid().ToString("N") + Path.GetExtension(inputPath))
            : outputFullPath;

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);

        byte[] resultBytes;
        var appliedCount = edits.Count;

        if (extension == ".mp3" || (inputBytes.Length >= 3 && inputBytes[0] == 0x49 && inputBytes[1] == 0x44 && inputBytes[2] == 0x33))
        {
            resultBytes = WriteMp3Edits(inputBytes, edits);
        }
        else
        {
            resultBytes = WriteWavEdits(inputBytes, edits);
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

        var afterDoc = await _reader.ReadAsync(outputFullPath, cancellationToken);
        var verifiedCount = 0;

        foreach (var edit in edits)
        {
            var match = afterDoc.Fields.FirstOrDefault(f =>
                f.Name.Equals(edit.Name, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(MapToStandardName(edit.Name), StringComparison.OrdinalIgnoreCase));
            if (match is not null && match.Value.Contains(edit.NewValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                verifiedCount++;
            }
        }

        var msg = $"Applied {appliedCount} edit(s). Verification: {verifiedCount}/{edits.Count} edits confirmed in output audio file.";
        return new WriteResult(inputPath, outputFullPath, appliedCount, 0, true, msg);
    }

    private static byte[] WriteMp3Edits(byte[] inputBytes, IReadOnlyList<MetadataEdit> edits)
    {
        // First scrub existing ID3 tag to start clean
        var cleanMp3 = ScrubMp3Data(inputBytes);
        var id3TagBytes = BuildId3v23Tag(edits);

        var result = new byte[id3TagBytes.Length + cleanMp3.Length];
        Array.Copy(id3TagBytes, 0, result, 0, id3TagBytes.Length);
        Array.Copy(cleanMp3, 0, result, id3TagBytes.Length, cleanMp3.Length);
        return result;
    }

    private static byte[] WriteWavEdits(byte[] inputBytes, IReadOnlyList<MetadataEdit> edits)
    {
        var cleanWav = ScrubWavData(inputBytes);
        var listInfoChunk = BuildWavListInfoChunk(edits);

        using var ms = new MemoryStream(cleanWav.Length + listInfoChunk.Length);
        ms.Write(cleanWav, 0, Math.Min(12, cleanWav.Length)); // Copy RIFF header
        ms.Write(listInfoChunk, 0, listInfoChunk.Length);     // Write LIST INFO chunk
        if (cleanWav.Length > 12)
        {
            ms.Write(cleanWav, 12, cleanWav.Length - 12);
        }

        var bytes = ms.ToArray();
        // Update RIFF payload length
        var payloadSize = bytes.Length - 8;
        BitConverter.GetBytes(payloadSize).CopyTo(bytes, 4);
        return bytes;
    }

    private static byte[] BuildId3v23Tag(IReadOnlyList<MetadataEdit> edits)
    {
        using var framesStream = new MemoryStream();

        foreach (var edit in edits)
        {
            var frameId = MapNameToId3Frame(edit.Name);
            if (frameId is null) continue;

            var valBytes = Encoding.UTF8.GetBytes(edit.NewValue);
            var framePayload = new byte[1 + valBytes.Length];
            framePayload[0] = 3; // UTF-8 encoding
            Array.Copy(valBytes, 0, framePayload, 1, valBytes.Length);

            var frameHeader = new byte[10];
            Encoding.ASCII.GetBytes(frameId).CopyTo(frameHeader, 0);
            var size = framePayload.Length;
            frameHeader[4] = (byte)((size >> 24) & 0xFF);
            frameHeader[5] = (byte)((size >> 16) & 0xFF);
            frameHeader[6] = (byte)((size >> 8) & 0xFF);
            frameHeader[7] = (byte)(size & 0xFF);

            framesStream.Write(frameHeader, 0, 10);
            framesStream.Write(framePayload, 0, framePayload.Length);
        }

        var framesData = framesStream.ToArray();
        var tagSize = framesData.Length;

        var header = new byte[10];
        header[0] = (byte)'I';
        header[1] = (byte)'D';
        header[2] = (byte)'3';
        header[3] = 3; // ID3v2.3
        header[4] = 0; // Flags

        // Synchsafe size
        header[6] = (byte)((tagSize >> 21) & 0x7F);
        header[7] = (byte)((tagSize >> 14) & 0x7F);
        header[8] = (byte)((tagSize >> 7) & 0x7F);
        header[9] = (byte)(tagSize & 0x7F);

        var result = new byte[10 + framesData.Length];
        Array.Copy(header, 0, result, 0, 10);
        Array.Copy(framesData, 0, result, 10, framesData.Length);
        return result;
    }

    private static byte[] BuildWavListInfoChunk(IReadOnlyList<MetadataEdit> edits)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("INFO"));

        foreach (var edit in edits)
        {
            var chunkId = MapNameToInfoChunk(edit.Name);
            if (chunkId is null) continue;

            var valBytes = Encoding.UTF8.GetBytes(edit.NewValue + "\0");
            var subHeader = new byte[8];
            Encoding.ASCII.GetBytes(chunkId).CopyTo(subHeader, 0);
            BitConverter.GetBytes(valBytes.Length).CopyTo(subHeader, 4);

            ms.Write(subHeader, 0, 8);
            ms.Write(valBytes, 0, valBytes.Length);

            if (valBytes.Length % 2 != 0)
            {
                ms.WriteByte(0); // Word alignment padding
            }
        }

        var infoData = ms.ToArray();
        var chunkHeader = new byte[8];
        Encoding.ASCII.GetBytes("LIST").CopyTo(chunkHeader, 0);
        BitConverter.GetBytes(infoData.Length).CopyTo(chunkHeader, 4);

        var result = new byte[8 + infoData.Length];
        Array.Copy(chunkHeader, 0, result, 0, 8);
        Array.Copy(infoData, 0, result, 8, infoData.Length);
        return result;
    }

    private static byte[] ScrubMp3Data(byte[] source)
    {
        var startOffset = 0;
        if (source.Length >= 10 && source[0] == 0x49 && source[1] == 0x44 && source[2] == 0x33)
        {
            var tagSize = (source[6] & 0x7F) << 21 | (source[7] & 0x7F) << 14 | (source[8] & 0x7F) << 7 | (source[9] & 0x7F);
            startOffset = 10 + tagSize;
            if (startOffset > source.Length) startOffset = 0;
        }

        var endOffset = source.Length;
        if (endOffset - startOffset >= 128)
        {
            var v1Pos = endOffset - 128;
            if (source[v1Pos] == 'T' && source[v1Pos + 1] == 'A' && source[v1Pos + 2] == 'G')
            {
                endOffset -= 128;
            }
        }

        var len = endOffset - startOffset;
        var res = new byte[len];
        Array.Copy(source, startOffset, res, 0, len);
        return res;
    }

    private static byte[] ScrubWavData(byte[] source)
    {
        if (source.Length < 12) return source;
        using var output = new MemoryStream(source.Length);
        output.Write(source, 0, 12);

        var pos = 12;
        while (pos + 8 <= source.Length)
        {
            var chunkId = Encoding.ASCII.GetString(source, pos, 4);
            var chunkSize = BitConverter.ToInt32(source, pos + 4);
            if (chunkSize < 0 || pos + 8 + chunkSize > source.Length) break;
            var chunkTotal = 8 + chunkSize + (chunkSize % 2);

            var isListInfo = chunkId == "LIST" && chunkSize >= 4 && Encoding.ASCII.GetString(source, pos + 8, 4) == "INFO";
            var isId3 = chunkId.Equals("id3 ", StringComparison.OrdinalIgnoreCase) || chunkId.Equals("ID3 ", StringComparison.OrdinalIgnoreCase);

            if (!isListInfo && !isId3)
            {
                output.Write(source, pos, Math.Min(chunkTotal, source.Length - pos));
            }

            pos += chunkTotal;
        }

        return output.ToArray();
    }

    private static string? MapNameToId3Frame(string name) => name.ToLowerInvariant() switch
    {
        "title" or "nam" or "inam" => "TIT2",
        "artist" or "art" or "iart" => "TPE1",
        "album" or "prd" or "iprd" => "TALB",
        "year" or "date" or "crd" or "icrd" => "TYER",
        "genre" or "gnr" or "ignr" => "TCON",
        "comment" or "cmt" or "icmt" => "COMM",
        "software" or "sft" or "isft" => "TENC",
        "copyright" or "cop" or "icop" => "TCOP",
        _ => null
    };

    private static string? MapNameToInfoChunk(string name) => name.ToLowerInvariant() switch
    {
        "title" or "nam" or "inam" => "INAM",
        "artist" or "art" or "iart" => "IART",
        "album" or "prd" or "iprd" => "IPRD",
        "year" or "date" or "crd" or "icrd" => "ICRD",
        "genre" or "gnr" or "ignr" => "IGNR",
        "comment" or "cmt" or "icmt" => "ICMT",
        "software" or "sft" or "isft" => "ISFT",
        "copyright" or "cop" or "icop" => "ICOP",
        _ => null
    };

    private static string MapToStandardName(string name) => name.ToLowerInvariant() switch
    {
        "title" or "inam" => "Title",
        "artist" or "iart" => "Artist",
        "album" or "iprd" => "Album",
        "year" or "icrd" => "Year",
        "genre" or "ignr" => "Genre",
        "comment" or "icmt" => "Comment",
        "software" or "isft" => "Software",
        "copyright" or "icop" => "Copyright",
        _ => name
    };
}
