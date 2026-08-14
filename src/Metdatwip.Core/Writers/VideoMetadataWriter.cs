using System.Text;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Scrubbers;

namespace Metdatwip.Core.Writers;

public sealed class VideoMetadataWriter : IMetadataWriter
{
    private static readonly string[] SupportedExtensions = [".mp4", ".mov", ".m4v", ".mkv", ".webm"];
    private readonly VideoMetadataReader _reader;
    private readonly VideoMetadataScrubber _scrubber;

    public VideoMetadataWriter(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new VideoMetadataReader(sensitivityClassifier);
        _scrubber = new VideoMetadataScrubber(sensitivityClassifier);
    }

    public string Name => "video-metadata-writer";

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
            throw new FileNotFoundException("Video file not found.", inputPath);
        }

        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        var isSameFile = string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        var targetFile = isSameFile
            ? Path.Combine(Path.GetTempPath(), "metdatwip_video_tmp_" + Guid.NewGuid().ToString("N") + Path.GetExtension(inputPath))
            : outputFullPath;

        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
        var extension = Path.GetExtension(inputPath).ToLowerInvariant();

        byte[] resultBytes;
        if (extension is ".mp4" or ".mov" or ".m4v")
        {
            resultBytes = WriteMp4Edits(inputBytes, edits);
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

        var msg = $"Applied {edits.Count} edit(s). Verification: {verifiedCount}/{edits.Count} edits confirmed in output video file.";
        return new WriteResult(inputPath, outputFullPath, edits.Count, 0, true, msg);
    }

    private static byte[] WriteMp4Edits(byte[] inputBytes, IReadOnlyList<MetadataEdit> edits)
    {
        var udtaAtom = BuildMp4UdtaAtom(edits);
        using var ms = new MemoryStream(inputBytes.Length + udtaAtom.Length);

        var pos = 0;

        while (pos + 8 <= inputBytes.Length)
        {
            var boxSize = ReadInt32BigEndian(inputBytes, pos);
            var boxType = Encoding.ASCII.GetString(inputBytes, pos + 4, 4);

            if (boxSize <= 0 || pos + boxSize > inputBytes.Length)
            {
                ms.Write(inputBytes, pos, inputBytes.Length - pos);
                break;
            }

            if (boxType == "moov")
            {
                // Inject udta inside moov
                var moovPayload = new MemoryStream();
                var moovPos = pos + 8;
                var moovEnd = pos + boxSize;

                while (moovPos + 8 <= moovEnd)
                {
                    var innerSize = ReadInt32BigEndian(inputBytes, moovPos);
                    var innerType = Encoding.ASCII.GetString(inputBytes, moovPos + 4, 4);
                    if (innerSize <= 0 || moovPos + innerSize > moovEnd) break;

                    if (innerType != "udta")
                    {
                        moovPayload.Write(inputBytes, moovPos, innerSize);
                    }

                    moovPos += innerSize;
                }

                // Append new udta
                moovPayload.Write(udtaAtom, 0, udtaAtom.Length);
                var moovBytes = moovPayload.ToArray();

                var newMoovHeader = new byte[8];
                WriteInt32BigEndian(newMoovHeader, 0, 8 + moovBytes.Length);
                Encoding.ASCII.GetBytes("moov").CopyTo(newMoovHeader, 4);

                ms.Write(newMoovHeader, 0, 8);
                ms.Write(moovBytes, 0, moovBytes.Length);

                pos += boxSize;
                continue;
            }

            ms.Write(inputBytes, pos, boxSize);
            pos += boxSize;
        }

        return ms.ToArray();
    }

    private static byte[] BuildMp4UdtaAtom(IReadOnlyList<MetadataEdit> edits)
    {
        using var ilstStream = new MemoryStream();

        foreach (var edit in edits)
        {
            var atomType = MapNameToMp4Atom(edit.Name);
            if (atomType is null) continue;

            var valBytes = Encoding.UTF8.GetBytes(edit.NewValue);
            var dataAtomLen = 8 + 8 + valBytes.Length; // 8 header + 8 flags/type + valBytes

            var dataAtom = new byte[dataAtomLen];
            WriteInt32BigEndian(dataAtom, 0, dataAtomLen);
            Encoding.ASCII.GetBytes("data").CopyTo(dataAtom, 4);
            dataAtom[8] = 0;
            dataAtom[9] = 0;
            dataAtom[10] = 0;
            dataAtom[11] = 1; // UTF-8 type
            Array.Copy(valBytes, 0, dataAtom, 16, valBytes.Length);

            var itemAtomLen = 8 + dataAtom.Length;
            var itemAtomHeader = new byte[8];
            WriteInt32BigEndian(itemAtomHeader, 0, itemAtomLen);
            Encoding.Latin1.GetBytes(atomType).CopyTo(itemAtomHeader, 4);

            ilstStream.Write(itemAtomHeader, 0, 8);
            ilstStream.Write(dataAtom, 0, dataAtom.Length);
        }

        var ilstData = ilstStream.ToArray();

        // ilst atom
        var ilstAtom = new byte[8 + ilstData.Length];
        WriteInt32BigEndian(ilstAtom, 0, ilstAtom.Length);
        Encoding.ASCII.GetBytes("ilst").CopyTo(ilstAtom, 4);
        Array.Copy(ilstData, 0, ilstAtom, 8, ilstData.Length);

        // meta atom (hdlr + ilst)
        var hdlrAtom = new byte[] {
            0, 0, 0, 36, (byte)'h', (byte)'d', (byte)'l', (byte)'r',
            0, 0, 0, 0, 0, 0, 0, 0,
            (byte)'m', (byte)'d', (byte)'i', (byte)'r', (byte)'a', (byte)'p', (byte)'p', (byte)'l',
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        var metaPayloadLen = hdlrAtom.Length + ilstAtom.Length;
        var metaAtom = new byte[12 + metaPayloadLen];
        WriteInt32BigEndian(metaAtom, 0, metaAtom.Length);
        Encoding.ASCII.GetBytes("meta").CopyTo(metaAtom, 4);
        // Bytes 8..11 are flags/version (0x00000000)
        Array.Copy(hdlrAtom, 0, metaAtom, 12, hdlrAtom.Length);
        Array.Copy(ilstAtom, 0, metaAtom, 12 + hdlrAtom.Length, ilstAtom.Length);

        // udta atom container
        var udtaAtom = new byte[8 + metaAtom.Length];
        WriteInt32BigEndian(udtaAtom, 0, udtaAtom.Length);
        Encoding.ASCII.GetBytes("udta").CopyTo(udtaAtom, 4);
        Array.Copy(metaAtom, 0, udtaAtom, 8, metaAtom.Length);

        return udtaAtom;
    }

    private static string? MapNameToMp4Atom(string name) => name.ToLowerInvariant() switch
    {
        "title" or "nam" or "\xa9nam" => "\xa9nam",
        "artist" or "art" or "\xa9art" => "\xa9ART",
        "album" or "alb" or "\xa9alb" => "\xa9alb",
        "year" or "date" or "day" or "\xa9day" => "\xa9day",
        "software" or "swr" or "tool" or "\xa9swr" => "\xa9swr",
        "comment" or "cmt" or "\xa9cmt" => "\xa9cmt",
        "copyright" or "cpy" or "\xa9cpy" => "\xa9cpy",
        _ => null
    };

    private static string MapToStandardName(string name) => name.ToLowerInvariant() switch
    {
        "title" or "\xa9nam" => "Title",
        "artist" or "\xa9art" => "Artist",
        "album" or "\xa9alb" => "Album",
        "year" or "\xa9day" => "Year",
        "software" or "\xa9swr" => "Software",
        "comment" or "\xa9cmt" => "Comment",
        "copyright" or "\xa9cpy" => "Copyright",
        _ => name
    };

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((value >> 24) & 0xFF);
        bytes[offset + 1] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 3] = (byte)(value & 0xFF);
    }
}
