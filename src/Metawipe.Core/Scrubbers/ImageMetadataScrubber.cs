using System.Buffers.Binary;
using System.Text;
using Metawipe.Core.Abstractions;
using Metawipe.Core.Models;
using Metawipe.Core.Readers;

namespace Metawipe.Core.Scrubbers;

/// <summary>
/// Lossless metadata scrubber for JPEG and PNG files.
/// </summary>
public sealed class ImageMetadataScrubber : IMetadataScrubber
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
    ];

    private readonly ImageMetadataReader _reader;

    public ImageMetadataScrubber(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new ImageMetadataReader(sensitivityClassifier);
    }

    /// <inheritdoc />
    public string Name => "image-metadata-scrubber";

    /// <inheritdoc />
    public bool CanScrub(string filePath, byte[]? magicBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension.ToLowerInvariant()))
        {
            return true;
        }

        return MatchesKnownMagic(magicBytes);
    }

    /// <inheritdoc />
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
            throw new FileNotFoundException("Input file not found.", inputPath);
        }

        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        if (string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Output path must be different from input path.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException("Unsupported image format. Supported: JPEG, PNG.");
        }

        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
        var retention = BuildRetentionPolicy(profile);

        var scrubbedBytes = extension switch
        {
            ".jpg" or ".jpeg" => ScrubJpeg(inputBytes, retention),
            ".png" => ScrubPng(inputBytes, retention),
            _ => throw new NotSupportedException("Unsupported image format. Supported: JPEG, PNG."),
        };

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllBytesAsync(outputPath, scrubbedBytes, cancellationToken);

        var beforeDocument = await _reader.ReadAsync(inputPath, cancellationToken);
        var afterDocument = await _reader.ReadAsync(outputPath, cancellationToken);

        var removedFields = Math.Max(0, beforeDocument.Fields.Count - afterDocument.Fields.Count);
        var keptFields = afterDocument.Fields.Count;
        var sensitiveRemaining = afterDocument.Fields.Count(field => field.IsSensitive);

        var message = sensitiveRemaining == 0
            ? "Verification scan: 0 sensitive fields remaining."
            : $"Verification scan: {sensitiveRemaining} sensitive field(s) remaining.";

        return new ScrubResult(inputPath, outputPath, removedFields, keptFields, true, message);
    }

    private static byte[] ScrubJpeg(byte[] source, RetentionPolicy retention)
    {
        if (source.Length < 4 || source[0] != 0xFF || source[1] != 0xD8)
        {
            throw new InvalidDataException("Invalid JPEG file: missing SOI marker.");
        }

        using var output = new MemoryStream(source.Length);
        output.Write(source, 0, 2); // SOI

        var position = 2;
        while (position < source.Length)
        {
            if (source[position] != 0xFF)
            {
                throw new InvalidDataException("Invalid JPEG file: expected marker prefix.");
            }

            while (position < source.Length && source[position] == 0xFF)
            {
                position++;
            }

            if (position >= source.Length)
            {
                break;
            }

            var marker = source[position];
            var segmentStart = position - 1;

            if (marker == 0xD9)
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xD9);
                return output.ToArray();
            }

            if (IsStandaloneJpegMarker(marker))
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                position++;
                continue;
            }

            if (position + 2 >= source.Length)
            {
                throw new InvalidDataException("Invalid JPEG file: truncated segment length.");
            }

            var segmentLength = (source[position + 1] << 8) | source[position + 2];
            var segmentTotalBytes = 2 + segmentLength;
            if (segmentStart + segmentTotalBytes > source.Length)
            {
                throw new InvalidDataException("Invalid JPEG file: segment length out of range.");
            }

            if (marker == 0xDA) // SOS
            {
                output.Write(source, segmentStart, segmentTotalBytes);
                position = segmentStart + segmentTotalBytes;
                return CopyJpegScanDataToEnd(source, output, position);
            }

            var payloadOffset = segmentStart + 4;
            var payloadLength = segmentLength - 2;
            var kind = DetectJpegMetadataKind(marker, source, payloadOffset, payloadLength);
            var keepSegment = kind is JpegMetadataKind.None || ShouldKeepJpegKind(retention, kind);
            if (keepSegment)
            {
                output.Write(source, segmentStart, segmentTotalBytes);
            }

            position = segmentStart + segmentTotalBytes;
        }

        return output.ToArray();
    }

    private static byte[] CopyJpegScanDataToEnd(byte[] source, MemoryStream output, int scanStart)
    {
        var position = scanStart;
        while (position + 1 < source.Length)
        {
            if (source[position] == 0xFF)
            {
                var next = source[position + 1];
                if (next == 0x00 || (next >= 0xD0 && next <= 0xD7))
                {
                    position += 2;
                    continue;
                }

                if (next == 0xD9)
                {
                    output.Write(source, scanStart, position - scanStart);
                    output.WriteByte(0xFF);
                    output.WriteByte(0xD9);
                    return output.ToArray();
                }
            }

            position++;
        }

        output.Write(source, scanStart, source.Length - scanStart);
        return output.ToArray();
    }

    private static byte[] ScrubPng(byte[] source, RetentionPolicy retention)
    {
        if (!MatchesPngMagic(source))
        {
            throw new InvalidDataException("Invalid PNG file: missing signature.");
        }

        using var output = new MemoryStream(source.Length);
        output.Write(source, 0, 8);

        var position = 8;
        while (position + 12 <= source.Length)
        {
            var chunkLength = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(position, 4));
            var chunkType = Encoding.ASCII.GetString(source, position + 4, 4);
            var chunkTotalBytes = 12 + chunkLength;

            if (position + chunkTotalBytes > source.Length)
            {
                throw new InvalidDataException("Invalid PNG file: chunk length out of range.");
            }

            var keepChunk = ShouldKeepPngChunk(retention, chunkType, source, position + 8, chunkLength);
            if (keepChunk)
            {
                output.Write(source, position, chunkTotalBytes);
            }

            position += chunkTotalBytes;
            if (string.Equals(chunkType, "IEND", StringComparison.Ordinal))
            {
                break;
            }
        }

        return output.ToArray();
    }

    private static bool ShouldKeepPngChunk(
        RetentionPolicy retention,
        string chunkType,
        byte[] source,
        int dataOffset,
        int dataLength)
    {
        return chunkType switch
        {
            "eXIf" => retention.KeepExifOrGps,
            "iCCP" => retention.KeepIcc,
            "iTXt" or "tEXt" or "zTXt" => ShouldKeepTextualChunk(retention, source, dataOffset, dataLength),
            _ => true,
        };
    }

    private static bool ShouldKeepTextualChunk(RetentionPolicy retention, byte[] source, int dataOffset, int dataLength)
    {
        var keyword = ReadPngKeyword(source, dataOffset, dataLength);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        if (keyword.Contains("xmp", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return retention.KeepXmp;
        }

        if (keyword.Contains("iptc", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("photoshop", StringComparison.OrdinalIgnoreCase))
        {
            return retention.KeepIptc;
        }

        return false;
    }

    private static string ReadPngKeyword(byte[] source, int dataOffset, int dataLength)
    {
        var max = dataOffset + dataLength;
        var end = dataOffset;
        while (end < max && source[end] != 0)
        {
            end++;
        }

        if (end <= dataOffset)
        {
            return string.Empty;
        }

        return Encoding.Latin1.GetString(source, dataOffset, end - dataOffset);
    }

    private static JpegMetadataKind DetectJpegMetadataKind(byte marker, byte[] source, int payloadOffset, int payloadLength)
    {
        if (payloadLength <= 0 || payloadOffset < 0 || payloadOffset + payloadLength > source.Length)
        {
            return JpegMetadataKind.None;
        }

        return marker switch
        {
            0xE1 when StartsWithAscii(source, payloadOffset, payloadLength, "http://ns.adobe.com/xap/1.0/\0") => JpegMetadataKind.Xmp,
            0xE1 when StartsWithAscii(source, payloadOffset, payloadLength, "Exif\0\0") => JpegMetadataKind.ExifOrGps,
            0xE1 => JpegMetadataKind.ExifOrGps,
            0xE2 when StartsWithAscii(source, payloadOffset, payloadLength, "ICC_PROFILE\0") => JpegMetadataKind.Icc,
            0xED => JpegMetadataKind.Iptc,
            _ => JpegMetadataKind.None,
        };
    }

    private static bool StartsWithAscii(byte[] source, int offset, int availableLength, string prefix)
    {
        var prefixBytes = Encoding.ASCII.GetBytes(prefix);
        if (availableLength < prefixBytes.Length)
        {
            return false;
        }

        for (var i = 0; i < prefixBytes.Length; i++)
        {
            if (source[offset + i] != prefixBytes[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldKeepJpegKind(RetentionPolicy retention, JpegMetadataKind kind)
    {
        return kind switch
        {
            JpegMetadataKind.ExifOrGps => retention.KeepExifOrGps,
            JpegMetadataKind.Xmp => retention.KeepXmp,
            JpegMetadataKind.Iptc => retention.KeepIptc,
            JpegMetadataKind.Icc => retention.KeepIcc,
            _ => true,
        };
    }

    private static RetentionPolicy BuildRetentionPolicy(ScrubProfile profile)
    {
        if (profile.Mode == ScrubProfileMode.StripAll)
        {
            return new RetentionPolicy(false, false, false, false);
        }

        var keepExif = HasWhitelistGroup(profile, "exif") || HasWhitelistGroup(profile, "gps");
        var keepXmp = HasWhitelistGroup(profile, "xmp");
        var keepIptc = HasWhitelistGroup(profile, "iptc");
        var keepIcc = HasWhitelistGroup(profile, "icc");

        return new RetentionPolicy(keepExif, keepXmp, keepIptc, keepIcc);
    }

    private static bool HasWhitelistGroup(ScrubProfile profile, string group)
    {
        var prefix = group.ToLowerInvariant() + "/";
        return profile.Whitelist.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsStandaloneJpegMarker(byte marker) =>
        marker is 0x01 or 0xD0 or 0xD1 or 0xD2 or 0xD3 or 0xD4 or 0xD5 or 0xD6 or 0xD7;

    private static bool MatchesKnownMagic(byte[]? magicBytes) =>
        MatchesJpegMagic(magicBytes) || MatchesPngMagic(magicBytes);

    private static bool MatchesJpegMagic(byte[]? magicBytes)
    {
        if (magicBytes is null || magicBytes.Length < 3)
        {
            return false;
        }

        return magicBytes[0] == 0xFF &&
               magicBytes[1] == 0xD8 &&
               magicBytes[2] == 0xFF;
    }

    private static bool MatchesPngMagic(byte[]? magicBytes)
    {
        if (magicBytes is null || magicBytes.Length < 8)
        {
            return false;
        }

        return magicBytes[0] == 0x89 && magicBytes[1] == 0x50 && magicBytes[2] == 0x4E && magicBytes[3] == 0x47 &&
               magicBytes[4] == 0x0D && magicBytes[5] == 0x0A && magicBytes[6] == 0x1A && magicBytes[7] == 0x0A;
    }

    private enum JpegMetadataKind
    {
        None = 0,
        ExifOrGps = 1,
        Xmp = 2,
        Iptc = 3,
        Icc = 4,
    }

    private readonly record struct RetentionPolicy(
        bool KeepExifOrGps,
        bool KeepXmp,
        bool KeepIptc,
        bool KeepIcc);
}
