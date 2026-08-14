using System.Text;

namespace Metdatwip.Core.Tests;

internal static class AudioTestFactory
{
    public static string CreateMp3WithMetadata(string targetPath, string title = "Test Song", string artist = "Test Artist", string album = "Test Album", string year = "2024")
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        using var ms = new MemoryStream();

        // 1. Build ID3v2.3 tag
        var framesStream = new MemoryStream();
        WriteId3Frame(framesStream, "TIT2", title);
        WriteId3Frame(framesStream, "TPE1", artist);
        WriteId3Frame(framesStream, "TALB", album);
        WriteId3Frame(framesStream, "TYER", year);

        var framesData = framesStream.ToArray();
        var tagHeader = new byte[10];
        tagHeader[0] = (byte)'I';
        tagHeader[1] = (byte)'D';
        tagHeader[2] = (byte)'3';
        tagHeader[3] = 3; // ID3v2.3
        tagHeader[4] = 0; // Flags

        var tagSize = framesData.Length;
        tagHeader[6] = (byte)((tagSize >> 21) & 0x7F);
        tagHeader[7] = (byte)((tagSize >> 14) & 0x7F);
        tagHeader[8] = (byte)((tagSize >> 7) & 0x7F);
        tagHeader[9] = (byte)(tagSize & 0x7F);

        ms.Write(tagHeader, 0, 10);
        ms.Write(framesData, 0, framesData.Length);

        // 2. Dummy MP3 audio frames (MPEG 1 Layer 3 sync header: 0xFF, 0xFB)
        for (var i = 0; i < 10; i++)
        {
            ms.WriteByte(0xFF);
            ms.WriteByte(0xFB);
            ms.WriteByte(0x90);
            ms.WriteByte(0x64);
            ms.Write(new byte[128], 0, 128);
        }

        // 3. ID3v1 tag (128 bytes at EOF)
        var v1Tag = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(v1Tag, 0);
        Encoding.Latin1.GetBytes(title.PadRight(30, '\0')).CopyTo(v1Tag, 3);
        Encoding.Latin1.GetBytes(artist.PadRight(30, '\0')).CopyTo(v1Tag, 33);
        Encoding.Latin1.GetBytes(album.PadRight(30, '\0')).CopyTo(v1Tag, 63);
        Encoding.Latin1.GetBytes(year.PadRight(4, '\0')).CopyTo(v1Tag, 93);
        ms.Write(v1Tag, 0, 128);

        File.WriteAllBytes(targetPath, ms.ToArray());
        return targetPath;
    }

    public static string CreateWavWithMetadata(string targetPath, string title = "WAV Title", string artist = "WAV Artist", string album = "WAV Album", string year = "2024")
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        using var ms = new MemoryStream();

        // RIFF header placeholder
        ms.Write(Encoding.ASCII.GetBytes("RIFF"));
        ms.Write(new byte[4]); // Length placeholder
        ms.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk (16 bytes PCM)
        ms.Write(Encoding.ASCII.GetBytes("fmt "));
        ms.Write(BitConverter.GetBytes(16)); // Chunk size
        ms.Write(BitConverter.GetBytes((short)1)); // PCM format
        ms.Write(BitConverter.GetBytes((short)2)); // 2 channels
        ms.Write(BitConverter.GetBytes(44100)); // Sample rate
        ms.Write(BitConverter.GetBytes(176400)); // Byte rate
        ms.Write(BitConverter.GetBytes((short)4)); // Block align
        ms.Write(BitConverter.GetBytes((short)16)); // Bits per sample

        // LIST INFO chunk
        using var infoStream = new MemoryStream();
        infoStream.Write(Encoding.ASCII.GetBytes("INFO"));
        WriteWavInfoSubChunk(infoStream, "INAM", title);
        WriteWavInfoSubChunk(infoStream, "IART", artist);
        WriteWavInfoSubChunk(infoStream, "IPRD", album);
        WriteWavInfoSubChunk(infoStream, "ICRD", year);

        var infoData = infoStream.ToArray();
        ms.Write(Encoding.ASCII.GetBytes("LIST"));
        ms.Write(BitConverter.GetBytes(infoData.Length));
        ms.Write(infoData, 0, infoData.Length);

        // data chunk
        var dummyData = new byte[256];
        ms.Write(Encoding.ASCII.GetBytes("data"));
        ms.Write(BitConverter.GetBytes(dummyData.Length));
        ms.Write(dummyData, 0, dummyData.Length);

        var wavBytes = ms.ToArray();
        var riffSize = wavBytes.Length - 8;
        BitConverter.GetBytes(riffSize).CopyTo(wavBytes, 4);

        File.WriteAllBytes(targetPath, wavBytes);
        return targetPath;
    }

    private static void WriteId3Frame(MemoryStream ms, string frameId, string value)
    {
        var valBytes = Encoding.UTF8.GetBytes(value);
        var payloadLen = 1 + valBytes.Length; // 1 byte encoding + value
        var framePayload = new byte[payloadLen];
        framePayload[0] = 3; // UTF-8
        Array.Copy(valBytes, 0, framePayload, 1, valBytes.Length);

        var frameHeader = new byte[10];
        Encoding.ASCII.GetBytes(frameId).CopyTo(frameHeader, 0);
        frameHeader[4] = (byte)((payloadLen >> 24) & 0xFF);
        frameHeader[5] = (byte)((payloadLen >> 16) & 0xFF);
        frameHeader[6] = (byte)((payloadLen >> 8) & 0xFF);
        frameHeader[7] = (byte)(payloadLen & 0xFF);

        ms.Write(frameHeader, 0, 10);
        ms.Write(framePayload, 0, framePayload.Length);
    }

    private static void WriteWavInfoSubChunk(MemoryStream ms, string subId, string value)
    {
        var valBytes = Encoding.UTF8.GetBytes(value + "\0");
        ms.Write(Encoding.ASCII.GetBytes(subId), 0, 4);
        ms.Write(BitConverter.GetBytes(valBytes.Length), 0, 4);
        ms.Write(valBytes, 0, valBytes.Length);
        if (valBytes.Length % 2 != 0)
        {
            ms.WriteByte(0);
        }
    }
}
