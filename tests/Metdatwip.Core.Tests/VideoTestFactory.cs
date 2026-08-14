using System.Text;
using Metdatwip.Core.Models;
using Metdatwip.Core.Writers;

namespace Metdatwip.Core.Tests;

internal static class VideoTestFactory
{
    public static string CreateMp4WithMetadata(string targetPath, string title = "Test Movie", string artist = "Test Director", string album = "Test Film Collection", string year = "2024")
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        using var ms = new MemoryStream();

        // 1. ftyp box
        var ftypBytes = new byte[]
        {
            0, 0, 0, 24, // 24 bytes
            (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
            0, 0, 2, 0,
            (byte)'m', (byte)'p', (byte)'4', (byte)'1',
            (byte)'i', (byte)'s', (byte)'o', (byte)'2',
        };
        ms.Write(ftypBytes, 0, ftypBytes.Length);

        // 2. moov box with udta and metadata
        var edits = new List<MetadataEdit>
        {
            new("MP4-Metadata", "Title", title),
            new("MP4-Metadata", "Artist", artist),
            new("MP4-Metadata", "Album", album),
            new("MP4-Metadata", "Year", year),
        };

        // Writer helper
        var writer = new VideoMetadataWriter(new Metdatwip.Core.Classification.RuleBasedSensitivityClassifier());

        // Dummy moov with mvhd box
        using var moovPayload = new MemoryStream();
        var mvhdBytes = new byte[108];
        mvhdBytes[3] = 108;
        Encoding.ASCII.GetBytes("mvhd").CopyTo(mvhdBytes, 4);
        moovPayload.Write(mvhdBytes, 0, 108);

        var rawMoovData = moovPayload.ToArray();
        var moovHeader = new byte[8];
        WriteInt32BigEndian(moovHeader, 0, 8 + rawMoovData.Length);
        Encoding.ASCII.GetBytes("moov").CopyTo(moovHeader, 4);
        ms.Write(moovHeader, 0, 8);
        ms.Write(rawMoovData, 0, rawMoovData.Length);

        // 3. mdat box
        var mdatDummy = new byte[512];
        var mdatHeader = new byte[8];
        WriteInt32BigEndian(mdatHeader, 0, 8 + mdatDummy.Length);
        Encoding.ASCII.GetBytes("mdat").CopyTo(mdatHeader, 4);
        ms.Write(mdatHeader, 0, 8);
        ms.Write(mdatDummy, 0, mdatDummy.Length);

        var initialFileBytes = ms.ToArray();
        File.WriteAllBytes(targetPath, initialFileBytes);

        // Use VideoMetadataWriter to write the udta metadata cleanly
        writer.WriteAsync(targetPath, targetPath, edits).GetAwaiter().GetResult();

        return targetPath;
    }

    public static string CreateMkvWithMetadata(string targetPath, string title = "MKV Movie", string artist = "MKV Director")
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        using var ms = new MemoryStream();
        // EBML magic: 1A 45 DF A3
        ms.WriteByte(0x1A);
        ms.WriteByte(0x45);
        ms.WriteByte(0xDF);
        ms.WriteByte(0xA3);

        var tagStr = $"TITLE={title}\0ARTIST={artist}\0SOFTWARE=Metdatwip Test\0";
        var tagBytes = Encoding.UTF8.GetBytes(tagStr);
        ms.Write(tagBytes, 0, tagBytes.Length);

        // Dummy payload
        ms.Write(new byte[256], 0, 256);

        File.WriteAllBytes(targetPath, ms.ToArray());
        return targetPath;
    }

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((value >> 24) & 0xFF);
        bytes[offset + 1] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 3] = (byte)(value & 0xFF);
    }
}
