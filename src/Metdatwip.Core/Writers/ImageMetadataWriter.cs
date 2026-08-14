using System.Buffers.Binary;
using System.Text;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Writers;

/// <summary>
/// Writes metadata edits to JPEG and PNG image files by modifying EXIF IFD entries and XMP packets
/// at the byte level, preserving pixel data losslessly.
/// </summary>
public sealed class ImageMetadataWriter : IMetadataWriter
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
    ];

    private readonly ImageMetadataReader _reader;

    /// <summary>
    /// Maps well-known EXIF tag names (lowercased) to their IFD tag numbers.
    /// Covers the most commonly edited EXIF/TIFF fields.
    /// </summary>
    private static readonly Dictionary<string, ushort> ExifTagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // IFD0 tags
        ["image description"]     = 0x010E,
        ["make"]                  = 0x010F,
        ["model"]                 = 0x0110,
        ["orientation"]           = 0x0112,
        ["x resolution"]          = 0x011A,
        ["y resolution"]          = 0x011B,
        ["resolution unit"]       = 0x0128,
        ["software"]              = 0x0131,
        ["date/time"]             = 0x0132,
        ["artist"]                = 0x013B,
        ["copyright"]             = 0x8298,
        // Exif Sub-IFD tags
        ["exposure time"]         = 0x829A,
        ["f-number"]              = 0x829D,
        ["iso speed ratings"]     = 0x8827,
        ["date/time original"]    = 0x9003,
        ["date/time digitized"]   = 0x9004,
        ["shutter speed value"]   = 0x9201,
        ["aperture value"]        = 0x9202,
        ["focal length"]          = 0x920A,
        ["color space"]           = 0xA001,
        ["exif image width"]      = 0xA002,
        ["exif image height"]     = 0xA003,
        ["body serial number"]    = 0xA431,
        ["lens make"]             = 0xA433,
        ["lens model"]            = 0xA434,
    };

    public ImageMetadataWriter(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new ImageMetadataReader(sensitivityClassifier);
    }

    /// <inheritdoc />
    public string Name => "image-metadata-writer";

    /// <inheritdoc />
    public bool CanWrite(string filePath, byte[]? magicBytes = null)
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
            throw new FileNotFoundException("Input file not found.", inputPath);
        }

        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        var isSameFile = string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        var targetFile = isSameFile ? Path.Combine(Path.GetTempPath(), "metdatwip_tmp_" + Guid.NewGuid().ToString("N") + Path.GetExtension(inputPath)) : outputFullPath;

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException("Unsupported image format. Supported: JPEG, PNG.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);

        // Separate edits by group
        var exifEdits = edits.Where(e =>
            e.Group.Equals("EXIF", StringComparison.OrdinalIgnoreCase) ||
            e.Group.Equals("GPS", StringComparison.OrdinalIgnoreCase)).ToList();
        var xmpEdits = edits.Where(e =>
            e.Group.Equals("XMP", StringComparison.OrdinalIgnoreCase)).ToList();

        var appliedEdits = 0;
        var skippedEdits = 0;

        byte[] resultBytes;

        if (extension is ".jpg" or ".jpeg")
        {
            resultBytes = ApplyJpegEdits(inputBytes, exifEdits, xmpEdits, ref appliedEdits, ref skippedEdits);
        }
        else // .png
        {
            resultBytes = ApplyPngEdits(inputBytes, exifEdits, xmpEdits, ref appliedEdits, ref skippedEdits);
        }

        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllBytesAsync(targetFile, resultBytes, cancellationToken);

        if (isSameFile)
        {
            File.Move(targetFile, outputFullPath, overwrite: true);
        }

        // Verification scan
        var afterDocument = await _reader.ReadAsync(outputPath, cancellationToken);
        var verifiedEdits = 0;
        foreach (var edit in edits)
        {
            var matchingField = afterDocument.Fields.FirstOrDefault(f =>
                string.Equals(f.Group, edit.Group, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Name, edit.Name, StringComparison.OrdinalIgnoreCase));

            if (matchingField is not null &&
                matchingField.Value.Contains(edit.NewValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                verifiedEdits++;
            }
        }

        var message = $"Applied {appliedEdits} edit(s), skipped {skippedEdits}. " +
                      $"Verification: {verifiedEdits}/{edits.Count} edits confirmed in output file.";

        return new WriteResult(inputPath, outputPath, appliedEdits, skippedEdits, true, message);
    }

    #region JPEG Editing

    /// <summary>
    /// Applies EXIF and XMP edits to a JPEG byte stream. If no APP1/EXIF segment exists,
    /// one is created from scratch to carry the requested EXIF tags.
    /// </summary>
    private static byte[] ApplyJpegEdits(
        byte[] source,
        List<MetadataEdit> exifEdits,
        List<MetadataEdit> xmpEdits,
        ref int appliedEdits,
        ref int skippedEdits)
    {
        if (source.Length < 4 || source[0] != 0xFF || source[1] != 0xD8)
        {
            throw new InvalidDataException("Invalid JPEG file: missing SOI marker.");
        }

        using var output = new MemoryStream(source.Length + 4096);
        output.Write(source, 0, 2); // SOI

        var position = 2;
        var exifApplied = false;
        var xmpApplied = false;

        while (position < source.Length)
        {
            if (source[position] != 0xFF)
            {
                throw new InvalidDataException("Invalid JPEG file: expected marker prefix.");
            }

            // Skip padding 0xFF bytes
            while (position < source.Length && source[position] == 0xFF)
            {
                position++;
            }

            if (position >= source.Length) break;

            var marker = source[position];
            var segmentStart = position - 1;

            if (marker == 0xD9) // EOI
            {
                // If no EXIF segment was found and we have edits, inject one before EOI
                if (!exifApplied && exifEdits.Count > 0)
                {
                    var newExifSegment = BuildExifApp1Segment(exifEdits, ref appliedEdits, ref skippedEdits);
                    output.Write(newExifSegment, 0, newExifSegment.Length);
                    exifApplied = true;
                }

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

            if (marker == 0xDA) // SOS — inject any remaining edits before scan data
            {
                if (!exifApplied && exifEdits.Count > 0)
                {
                    var newExifSegment = BuildExifApp1Segment(exifEdits, ref appliedEdits, ref skippedEdits);
                    output.Write(newExifSegment, 0, newExifSegment.Length);
                    exifApplied = true;
                }

                output.Write(source, segmentStart, segmentTotalBytes);
                position = segmentStart + segmentTotalBytes;
                return CopyJpegScanDataToEnd(source, output, position);
            }

            var payloadOffset = segmentStart + 4;
            var payloadLength = segmentLength - 2;

            // APP1 EXIF segment
            if (marker == 0xE1 && payloadLength >= 6 &&
                StartsWithAscii(source, payloadOffset, payloadLength, "Exif\0\0"))
            {
                if (exifEdits.Count > 0 && !exifApplied)
                {
                    var editedSegment = EditExifApp1Segment(source, segmentStart, segmentTotalBytes,
                        exifEdits, ref appliedEdits, ref skippedEdits);
                    output.Write(editedSegment, 0, editedSegment.Length);
                    exifApplied = true;
                }
                else
                {
                    output.Write(source, segmentStart, segmentTotalBytes);
                }

                position = segmentStart + segmentTotalBytes;
                continue;
            }

            // APP1 XMP segment
            if (marker == 0xE1 && payloadLength >= 29 &&
                StartsWithAscii(source, payloadOffset, payloadLength, "http://ns.adobe.com/xap/1.0/\0"))
            {
                if (xmpEdits.Count > 0 && !xmpApplied)
                {
                    var editedSegment = EditXmpApp1Segment(source, segmentStart, segmentTotalBytes,
                        payloadOffset, payloadLength, xmpEdits, ref appliedEdits, ref skippedEdits);
                    output.Write(editedSegment, 0, editedSegment.Length);
                    xmpApplied = true;
                }
                else
                {
                    output.Write(source, segmentStart, segmentTotalBytes);
                }

                position = segmentStart + segmentTotalBytes;
                continue;
            }

            // Copy other segments unchanged
            output.Write(source, segmentStart, segmentTotalBytes);
            position = segmentStart + segmentTotalBytes;
        }

        return output.ToArray();
    }

    /// <summary>
    /// Edits EXIF tags inside an existing APP1 segment by locating IFD entries
    /// and rewriting them with new string values.
    /// </summary>
    private static byte[] EditExifApp1Segment(
        byte[] source,
        int segmentStart,
        int segmentTotalBytes,
        List<MetadataEdit> exifEdits,
        ref int appliedEdits,
        ref int skippedEdits)
    {
        // Copy the original segment as a working buffer
        var segment = new byte[segmentTotalBytes];
        Array.Copy(source, segmentStart, segment, 0, segmentTotalBytes);

        // The TIFF data starts after: FF E1 (2) + length (2) + "Exif\0\0" (6) = offset 10 within segment
        var tiffOffset = 10;
        if (tiffOffset + 8 > segment.Length)
        {
            // Segment too small — build a new one
            return BuildExifApp1Segment(exifEdits, ref appliedEdits, ref skippedEdits);
        }

        var isLittleEndian = segment[tiffOffset] == 0x49 && segment[tiffOffset + 1] == 0x49;

        // Collect remaining edits that could not be applied in-place
        var remainingEdits = new List<MetadataEdit>();

        foreach (var edit in exifEdits)
        {
            if (!ExifTagMap.TryGetValue(edit.Name, out var targetTagId))
            {
                remainingEdits.Add(edit);
                continue;
            }

            var applied = TryEditIfdTag(segment, tiffOffset, isLittleEndian, targetTagId, edit.NewValue);
            if (applied)
            {
                appliedEdits++;
            }
            else
            {
                remainingEdits.Add(edit);
            }
        }

        // For remaining edits that couldn't be applied in-place, we rebuild the segment
        // with the original data plus new entries
        if (remainingEdits.Count > 0)
        {
            // Try to build a combined segment
            var rebuiltSegment = RebuildExifWithNewTags(segment, tiffOffset, isLittleEndian,
                remainingEdits, ref appliedEdits, ref skippedEdits);
            return rebuiltSegment;
        }

        // Recalculate segment length
        var newLength = segment.Length - 2; // exclude the FF E1 marker bytes
        segment[2] = (byte)((newLength >> 8) & 0xFF);
        segment[3] = (byte)(newLength & 0xFF);

        return segment;
    }

    /// <summary>
    /// Attempts to modify a tag value in-place within an IFD. Only works for ASCII string tags
    /// where the new value fits within the existing allocation.
    /// </summary>
    private static bool TryEditIfdTag(byte[] segment, int tiffOffset, bool isLittleEndian, ushort targetTagId, string newValue)
    {
        if (tiffOffset + 8 > segment.Length) return false;

        var ifd0Offset = ReadUInt32(segment, tiffOffset + 4, isLittleEndian);
        return TryEditIfdTagAtOffset(segment, tiffOffset, (int)ifd0Offset, isLittleEndian, targetTagId, newValue)
            || TryEditIfdTagInSubIfd(segment, tiffOffset, (int)ifd0Offset, isLittleEndian, targetTagId, newValue);
    }

    private static bool TryEditIfdTagAtOffset(byte[] segment, int tiffOffset, int ifdRelativeOffset,
        bool isLittleEndian, ushort targetTagId, string newValue)
    {
        var ifdAbsolute = tiffOffset + ifdRelativeOffset;
        if (ifdAbsolute + 2 > segment.Length) return false;

        var entryCount = ReadUInt16(segment, ifdAbsolute, isLittleEndian);
        var entriesStart = ifdAbsolute + 2;

        for (var i = 0; i < entryCount; i++)
        {
            var entryPos = entriesStart + (i * 12);
            if (entryPos + 12 > segment.Length) break;

            var tagId = ReadUInt16(segment, entryPos, isLittleEndian);
            if (tagId != targetTagId) continue;

            var tagType = ReadUInt16(segment, entryPos + 2, isLittleEndian);
            var count = ReadUInt32(segment, entryPos + 4, isLittleEndian);

            // ASCII string type (2) — write the new value
            if (tagType == 2)
            {
                var valueBytes = Encoding.ASCII.GetBytes(newValue + "\0");

                if (count <= 4)
                {
                    // Value is inline in the 4-byte value/offset field
                    if (valueBytes.Length <= 4)
                    {
                        WriteUInt32(segment, entryPos + 4, (uint)valueBytes.Length, isLittleEndian);
                        // Clear and write inline
                        segment[entryPos + 8] = 0;
                        segment[entryPos + 9] = 0;
                        segment[entryPos + 10] = 0;
                        segment[entryPos + 11] = 0;
                        Array.Copy(valueBytes, 0, segment, entryPos + 8, valueBytes.Length);
                        return true;
                    }
                    // New value won't fit inline — skip (will be handled by rebuild)
                    return false;
                }

                // Value is at an offset
                var valueOffset = ReadUInt32(segment, entryPos + 8, isLittleEndian);
                var valueAbsolute = tiffOffset + (int)valueOffset;

                if (valueAbsolute + valueBytes.Length <= segment.Length && valueBytes.Length <= (int)count)
                {
                    // Fits in existing allocation — write in place
                    WriteUInt32(segment, entryPos + 4, (uint)valueBytes.Length, isLittleEndian);
                    Array.Copy(valueBytes, 0, segment, valueAbsolute, valueBytes.Length);
                    // Zero-fill remaining bytes
                    for (var j = valueBytes.Length; j < (int)count; j++)
                    {
                        segment[valueAbsolute + j] = 0;
                    }
                    return true;
                }

                // Doesn't fit — needs rebuild
                return false;
            }

            // For SHORT (3), LONG (4) — handle simple numeric overwrites
            if ((tagType == 3 || tagType == 4) && uint.TryParse(newValue, out var numericValue))
            {
                if (tagType == 3 && count == 1) // SHORT
                {
                    WriteUInt16(segment, entryPos + 8, (ushort)numericValue, isLittleEndian);
                    return true;
                }
                if (tagType == 4 && count == 1) // LONG
                {
                    WriteUInt32(segment, entryPos + 8, numericValue, isLittleEndian);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks for an ExifSubIFD pointer (tag 0x8769) in IFD0, then searches that sub-IFD.
    /// </summary>
    private static bool TryEditIfdTagInSubIfd(byte[] segment, int tiffOffset, int ifd0RelativeOffset,
        bool isLittleEndian, ushort targetTagId, string newValue)
    {
        var ifd0Absolute = tiffOffset + ifd0RelativeOffset;
        if (ifd0Absolute + 2 > segment.Length) return false;

        var entryCount = ReadUInt16(segment, ifd0Absolute, isLittleEndian);
        var entriesStart = ifd0Absolute + 2;

        for (var i = 0; i < entryCount; i++)
        {
            var entryPos = entriesStart + (i * 12);
            if (entryPos + 12 > segment.Length) break;

            var tagId = ReadUInt16(segment, entryPos, isLittleEndian);
            if (tagId == 0x8769) // ExifSubIFD pointer
            {
                var subIfdOffset = (int)ReadUInt32(segment, entryPos + 8, isLittleEndian);
                return TryEditIfdTagAtOffset(segment, tiffOffset, subIfdOffset, isLittleEndian, targetTagId, newValue);
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a complete APP1 EXIF segment from scratch containing the requested tags.
    /// Used when no existing EXIF segment is present, or when in-place editing is not possible.
    /// </summary>
    private static byte[] BuildExifApp1Segment(
        List<MetadataEdit> edits,
        ref int appliedEdits,
        ref int skippedEdits)
    {
        // Resolve which edits we can fulfill
        var resolvedEdits = new List<(ushort tagId, string value)>();
        foreach (var edit in edits)
        {
            if (ExifTagMap.TryGetValue(edit.Name, out var tagId))
            {
                resolvedEdits.Add((tagId, edit.NewValue));
            }
            else
            {
                skippedEdits++;
            }
        }

        if (resolvedEdits.Count == 0)
        {
            return [];
        }

        // Separate IFD0 and ExifSubIFD tags
        var ifd0Tags = resolvedEdits.Where(e => e.tagId < 0x8000).OrderBy(e => e.tagId).ToList();
        var exifSubTags = resolvedEdits.Where(e => e.tagId >= 0x8000).OrderBy(e => e.tagId).ToList();

        var hasExifSub = exifSubTags.Count > 0;
        var ifd0EntryCount = ifd0Tags.Count + (hasExifSub ? 1 : 0); // +1 for ExifSubIFD pointer

        // Build TIFF structure (little-endian)
        using var tiffStream = new MemoryStream();
        var writer = new BinaryWriter(tiffStream);

        // TIFF header
        writer.Write((byte)0x49); // 'I' (little-endian)
        writer.Write((byte)0x49);
        writer.Write((ushort)42); // TIFF magic
        writer.Write((uint)8);    // Offset to IFD0

        // IFD0
        writer.Write((ushort)ifd0EntryCount);

        var ifd0DataAreaOffset = 8 + 2 + (ifd0EntryCount * 12) + 4; // header + count + entries + next-ifd-offset
        var dataArea = new MemoryStream();

        foreach (var (tagId, value) in ifd0Tags)
        {
            WriteAsciiIfdEntry(writer, tagId, value, ref ifd0DataAreaOffset, dataArea);
            appliedEdits++;
        }

        // ExifSubIFD pointer entry
        if (hasExifSub)
        {
            var exifSubIfdOffset = ifd0DataAreaOffset;
            writer.Write((ushort)0x8769);  // ExifSubIFD tag
            writer.Write((ushort)4);       // LONG type
            writer.Write((uint)1);         // count
            writer.Write((uint)exifSubIfdOffset);
        }

        // Next IFD offset (0 = no more IFDs)
        writer.Write((uint)0);

        // Write IFD0 data area
        writer.Write(dataArea.ToArray());

        // ExifSubIFD
        if (hasExifSub)
        {
            var exifSubDataAreaOffset = (int)tiffStream.Position + 2 + (exifSubTags.Count * 12) + 4;
            var exifSubData = new MemoryStream();

            writer.Write((ushort)exifSubTags.Count);

            foreach (var (tagId, value) in exifSubTags)
            {
                WriteAsciiIfdEntry(writer, tagId, value, ref exifSubDataAreaOffset, exifSubData);
                appliedEdits++;
            }

            writer.Write((uint)0); // Next IFD offset
            writer.Write(exifSubData.ToArray());
        }

        var tiffData = tiffStream.ToArray();

        // Build APP1 segment: FF E1 [length] Exif\0\0 [tiff data]
        using var segmentStream = new MemoryStream();
        segmentStream.WriteByte(0xFF);
        segmentStream.WriteByte(0xE1);

        var segmentPayloadLength = 2 + 6 + tiffData.Length; // length field + Exif\0\0 + tiff
        segmentStream.WriteByte((byte)((segmentPayloadLength >> 8) & 0xFF));
        segmentStream.WriteByte((byte)(segmentPayloadLength & 0xFF));

        // Exif header
        segmentStream.Write(Encoding.ASCII.GetBytes("Exif\0\0"));

        // TIFF data
        segmentStream.Write(tiffData);

        return segmentStream.ToArray();
    }

    private static void WriteAsciiIfdEntry(BinaryWriter writer, ushort tagId, string value,
        ref int dataAreaOffset, MemoryStream dataArea)
    {
        var valueBytes = Encoding.ASCII.GetBytes(value + "\0");

        writer.Write(tagId);
        writer.Write((ushort)2); // ASCII type
        writer.Write((uint)valueBytes.Length);

        if (valueBytes.Length <= 4)
        {
            // Inline
            var padded = new byte[4];
            Array.Copy(valueBytes, padded, valueBytes.Length);
            writer.Write(padded);
        }
        else
        {
            // Offset to data area
            writer.Write((uint)dataAreaOffset);
            dataArea.Write(valueBytes);
            // Align to even boundary
            if (valueBytes.Length % 2 != 0)
            {
                dataArea.WriteByte(0);
                dataAreaOffset += valueBytes.Length + 1;
            }
            else
            {
                dataAreaOffset += valueBytes.Length;
            }
        }
    }

    /// <summary>
    /// Rebuilds an EXIF APP1 segment from an existing one, preserving original entries
    /// and adding new tag entries from the provided edits.
    /// </summary>
    private static byte[] RebuildExifWithNewTags(byte[] originalSegment, int tiffOffset, bool isLittleEndian,
        List<MetadataEdit> newEdits, ref int appliedEdits, ref int skippedEdits)
    {
        // Strategy: extract existing TIFF data, parse IFD0 entries, merge new tags,
        // rebuild the whole TIFF structure.

        // For simplicity, extract all existing tags and rebuild
        var existingTags = ExtractExistingTags(originalSegment, tiffOffset, isLittleEndian);

        // Add new tags
        foreach (var edit in newEdits)
        {
            if (ExifTagMap.TryGetValue(edit.Name, out var tagId))
            {
                existingTags[tagId] = edit.NewValue;
                appliedEdits++;
            }
            else
            {
                skippedEdits++;
            }
        }

        // Rebuild from the merged tag set
        var mergedEdits = existingTags.Select(kv =>
        {
            var name = ExifTagMap.FirstOrDefault(m => m.Value == kv.Key).Key ?? $"tag-0x{kv.Key:X4}";
            return new MetadataEdit("EXIF", name, kv.Value);
        }).ToList();

        var dummyApplied = 0;
        var dummySkipped = 0;
        return BuildExifApp1Segment(mergedEdits, ref dummyApplied, ref dummySkipped);
    }

    /// <summary>
    /// Reads all ASCII-type tags from IFD0 of an existing EXIF segment.
    /// </summary>
    private static Dictionary<ushort, string> ExtractExistingTags(byte[] segment, int tiffOffset, bool isLittleEndian)
    {
        var tags = new Dictionary<ushort, string>();

        if (tiffOffset + 8 > segment.Length) return tags;

        var ifd0Offset = (int)ReadUInt32(segment, tiffOffset + 4, isLittleEndian);
        ExtractTagsFromIfd(segment, tiffOffset, ifd0Offset, isLittleEndian, tags);

        // Also check ExifSubIFD
        var ifd0Absolute = tiffOffset + ifd0Offset;
        if (ifd0Absolute + 2 <= segment.Length)
        {
            var entryCount = ReadUInt16(segment, ifd0Absolute, isLittleEndian);
            for (var i = 0; i < entryCount; i++)
            {
                var entryPos = ifd0Absolute + 2 + (i * 12);
                if (entryPos + 12 > segment.Length) break;
                var tagId = ReadUInt16(segment, entryPos, isLittleEndian);
                if (tagId == 0x8769) // ExifSubIFD
                {
                    var subIfdOffset = (int)ReadUInt32(segment, entryPos + 8, isLittleEndian);
                    ExtractTagsFromIfd(segment, tiffOffset, subIfdOffset, isLittleEndian, tags);
                }
            }
        }

        return tags;
    }

    private static void ExtractTagsFromIfd(byte[] segment, int tiffOffset, int ifdRelativeOffset,
        bool isLittleEndian, Dictionary<ushort, string> tags)
    {
        var ifdAbsolute = tiffOffset + ifdRelativeOffset;
        if (ifdAbsolute + 2 > segment.Length) return;

        var entryCount = ReadUInt16(segment, ifdAbsolute, isLittleEndian);
        for (var i = 0; i < entryCount; i++)
        {
            var entryPos = ifdAbsolute + 2 + (i * 12);
            if (entryPos + 12 > segment.Length) break;

            var tagId = ReadUInt16(segment, entryPos, isLittleEndian);
            var tagType = ReadUInt16(segment, entryPos + 2, isLittleEndian);
            var count = (int)ReadUInt32(segment, entryPos + 4, isLittleEndian);

            if (tagType == 2) // ASCII
            {
                string value;
                if (count <= 4)
                {
                    value = Encoding.ASCII.GetString(segment, entryPos + 8, Math.Min(count, 4)).TrimEnd('\0');
                }
                else
                {
                    var offset = tiffOffset + (int)ReadUInt32(segment, entryPos + 8, isLittleEndian);
                    if (offset + count <= segment.Length)
                    {
                        value = Encoding.ASCII.GetString(segment, offset, count).TrimEnd('\0');
                    }
                    else
                    {
                        continue;
                    }
                }

                tags[tagId] = value;
            }
        }
    }

    #endregion

    #region XMP Editing

    /// <summary>
    /// Edits XMP metadata in a JPEG APP1 XMP segment using simple string replacement
    /// on the XML-encoded XMP packet.
    /// </summary>
    private static byte[] EditXmpApp1Segment(
        byte[] source,
        int segmentStart,
        int segmentTotalBytes,
        int payloadOffset,
        int payloadLength,
        List<MetadataEdit> xmpEdits,
        ref int appliedEdits,
        ref int skippedEdits)
    {
        // XMP payload starts after "http://ns.adobe.com/xap/1.0/\0" header (29 bytes)
        var xmpHeaderLen = 29;
        var xmpXmlOffset = payloadOffset + xmpHeaderLen;
        var xmpXmlLength = payloadLength - xmpHeaderLen;

        if (xmpXmlLength <= 0)
        {
            skippedEdits += xmpEdits.Count;
            return source.AsSpan(segmentStart, segmentTotalBytes).ToArray();
        }

        var xmpXml = Encoding.UTF8.GetString(source, xmpXmlOffset, xmpXmlLength);

        foreach (var edit in xmpEdits)
        {
            // Try to find and replace XMP property values using common patterns
            var replaced = TryReplaceXmpProperty(ref xmpXml, edit.Name, edit.NewValue);
            if (replaced)
            {
                appliedEdits++;
            }
            else
            {
                skippedEdits++;
            }
        }

        // Rebuild segment
        var xmpBytes = Encoding.UTF8.GetBytes(xmpXml);
        var headerBytes = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");
        var newPayload = new byte[headerBytes.Length + xmpBytes.Length];
        Array.Copy(headerBytes, newPayload, headerBytes.Length);
        Array.Copy(xmpBytes, 0, newPayload, headerBytes.Length, xmpBytes.Length);

        var newSegmentLength = 2 + newPayload.Length;
        using var segOut = new MemoryStream();
        segOut.WriteByte(0xFF);
        segOut.WriteByte(0xE1);
        segOut.WriteByte((byte)((newSegmentLength >> 8) & 0xFF));
        segOut.WriteByte((byte)(newSegmentLength & 0xFF));
        segOut.Write(newPayload);

        return segOut.ToArray();
    }

    /// <summary>
    /// Attempts to replace an XMP property value using common XML patterns
    /// like &lt;ns:PropertyName&gt;value&lt;/ns:PropertyName&gt;.
    /// </summary>
    private static bool TryReplaceXmpProperty(ref string xmpXml, string propertyName, string newValue)
    {
        // Try common XMP patterns: <ns:Name>old</ns:Name>
        // Pattern 1: with namespace prefix
        var patterns = new[]
        {
            $":{propertyName}>",       // <dc:Creator>, <xmp:CreatorTool>, etc.
            $"\"{propertyName}\"",      // xmp:PropertyName="value" attribute style
        };

        foreach (var pattern in patterns)
        {
            var idx = xmpXml.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            // Element style: <ns:Name>value</ns:Name>
            if (pattern.EndsWith(">"))
            {
                var valueStart = idx + pattern.Length;
                var closeTag = xmpXml.IndexOf("</", valueStart, StringComparison.Ordinal);
                if (closeTag > valueStart)
                {
                    xmpXml = string.Concat(xmpXml.AsSpan(0, valueStart), newValue, xmpXml.AsSpan(closeTag));
                    return true;
                }
            }

            // Attribute style: name="value"
            if (pattern.StartsWith("\"") && pattern.EndsWith("\""))
            {
                var equalsIdx = xmpXml.IndexOf('=', idx);
                if (equalsIdx > 0 && equalsIdx < idx + pattern.Length + 5)
                {
                    var quoteChar = xmpXml[equalsIdx + 1];
                    var valueStart = equalsIdx + 2;
                    var valueEnd = xmpXml.IndexOf(quoteChar, valueStart);
                    if (valueEnd > valueStart)
                    {
                        xmpXml = string.Concat(xmpXml.AsSpan(0, valueStart), newValue, xmpXml.AsSpan(valueEnd));
                        return true;
                    }
                }
            }
        }

        return false;
    }

    #endregion

    #region PNG Editing

    private static byte[] ApplyPngEdits(
        byte[] source,
        List<MetadataEdit> exifEdits,
        List<MetadataEdit> xmpEdits,
        ref int appliedEdits,
        ref int skippedEdits)
    {
        if (!MatchesPngMagic(source))
        {
            throw new InvalidDataException("Invalid PNG file: missing signature.");
        }

        using var output = new MemoryStream(source.Length + 4096);
        output.Write(source, 0, 8); // PNG signature

        var position = 8;
        var exifApplied = false;

        while (position + 12 <= source.Length)
        {
            var chunkLength = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(position, 4));
            var chunkType = Encoding.ASCII.GetString(source, position + 4, 4);
            var chunkTotalBytes = 12 + chunkLength;

            if (position + chunkTotalBytes > source.Length)
            {
                throw new InvalidDataException("Invalid PNG file: chunk length out of range.");
            }

            // For eXIf chunks — edit EXIF within the chunk
            if (string.Equals(chunkType, "eXIf", StringComparison.Ordinal) && exifEdits.Count > 0 && !exifApplied)
            {
                var tiffData = new byte[chunkLength];
                Array.Copy(source, position + 8, tiffData, 0, chunkLength);

                var editedTiff = EditTiffData(tiffData, exifEdits, ref appliedEdits, ref skippedEdits);
                WritePngChunk(output, "eXIf", editedTiff);
                exifApplied = true;

                position += chunkTotalBytes;
                if (string.Equals(chunkType, "IEND", StringComparison.Ordinal)) break;
                continue;
            }

            // Before IDAT or IEND — inject eXIf chunk if we haven't edited EXIF yet
            if ((string.Equals(chunkType, "IDAT", StringComparison.Ordinal) ||
                 string.Equals(chunkType, "IEND", StringComparison.Ordinal)) &&
                exifEdits.Count > 0 && !exifApplied)
            {
                var newTiff = BuildTiffData(exifEdits, ref appliedEdits, ref skippedEdits);
                if (newTiff.Length > 0)
                {
                    WritePngChunk(output, "eXIf", newTiff);
                }
                exifApplied = true;
            }

            // Copy chunk as-is
            output.Write(source, position, chunkTotalBytes);

            position += chunkTotalBytes;
            if (string.Equals(chunkType, "IEND", StringComparison.Ordinal)) break;
        }

        // XMP edits for PNG are skipped (would require iTXt chunk manipulation)
        skippedEdits += xmpEdits.Count;

        return output.ToArray();
    }

    private static byte[] EditTiffData(byte[] tiffData, List<MetadataEdit> edits,
        ref int appliedEdits, ref int skippedEdits)
    {
        if (tiffData.Length < 8) return tiffData;

        var isLittleEndian = tiffData[0] == 0x49 && tiffData[1] == 0x49;

        // Try in-place edits
        var remaining = new List<MetadataEdit>();
        foreach (var edit in edits)
        {
            if (!ExifTagMap.TryGetValue(edit.Name, out var tagId))
            {
                skippedEdits++;
                continue;
            }

            var ifd0Offset = (int)ReadUInt32(tiffData, 4, isLittleEndian);
            var applied = TryEditIfdTagAtOffset(tiffData, 0, ifd0Offset, isLittleEndian, tagId, edit.NewValue);
            if (applied)
            {
                appliedEdits++;
            }
            else
            {
                remaining.Add(edit);
            }
        }

        if (remaining.Count > 0)
        {
            // Rebuild TIFF data with full edits list to ensure both IFD0 and ExifSubIFD tags are preserved
            return BuildTiffData(edits, ref appliedEdits, ref skippedEdits);
        }

        return tiffData;
    }

    private static byte[] BuildTiffData(List<MetadataEdit> edits, ref int appliedEdits, ref int skippedEdits)
    {
        var resolvedEdits = new List<(ushort tagId, string value)>();
        foreach (var edit in edits)
        {
            if (ExifTagMap.TryGetValue(edit.Name, out var tagId))
            {
                resolvedEdits.Add((tagId, edit.NewValue));
            }
            else
            {
                skippedEdits++;
            }
        }

        if (resolvedEdits.Count == 0) return [];

        // Separate IFD0 and ExifSubIFD tags
        var ifd0Tags = resolvedEdits.Where(e => e.tagId < 0x8000).OrderBy(e => e.tagId).ToList();
        var exifSubTags = resolvedEdits.Where(e => e.tagId >= 0x8000).OrderBy(e => e.tagId).ToList();

        var hasExifSub = exifSubTags.Count > 0;
        var ifd0EntryCount = ifd0Tags.Count + (hasExifSub ? 1 : 0);

        using var tiffStream = new MemoryStream();
        var writer = new BinaryWriter(tiffStream);

        // TIFF header (little-endian)
        writer.Write((byte)0x49);
        writer.Write((byte)0x49);
        writer.Write((ushort)42);
        writer.Write((uint)8);

        // IFD0
        writer.Write((ushort)ifd0EntryCount);

        var ifd0DataAreaOffset = 8 + 2 + (ifd0EntryCount * 12) + 4;
        var dataArea = new MemoryStream();

        foreach (var (tagId, value) in ifd0Tags)
        {
            WriteAsciiIfdEntry(writer, tagId, value, ref ifd0DataAreaOffset, dataArea);
            appliedEdits++;
        }

        // ExifSubIFD pointer entry
        if (hasExifSub)
        {
            var exifSubIfdOffset = ifd0DataAreaOffset;
            writer.Write((ushort)0x8769);
            writer.Write((ushort)4);
            writer.Write((uint)1);
            writer.Write((uint)exifSubIfdOffset);
        }

        // Next IFD offset
        writer.Write((uint)0);
        writer.Write(dataArea.ToArray());

        // ExifSubIFD
        if (hasExifSub)
        {
            var exifSubDataAreaOffset = (int)tiffStream.Position + 2 + (exifSubTags.Count * 12) + 4;
            var exifSubData = new MemoryStream();

            writer.Write((ushort)exifSubTags.Count);

            foreach (var (tagId, value) in exifSubTags)
            {
                WriteAsciiIfdEntry(writer, tagId, value, ref exifSubDataAreaOffset, exifSubData);
                appliedEdits++;
            }

            writer.Write((uint)0);
            writer.Write(exifSubData.ToArray());
        }

        return tiffStream.ToArray();
    }

    private static void WritePngChunk(MemoryStream output, string chunkType, byte[] data)
    {
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        output.Write(lengthBytes);

        var typeBytes = Encoding.ASCII.GetBytes(chunkType);
        output.Write(typeBytes);
        output.Write(data);

        // Calculate CRC32 over type + data
        var crcInput = new byte[4 + data.Length];
        Array.Copy(typeBytes, crcInput, 4);
        Array.Copy(data, 0, crcInput, 4, data.Length);
        var crc = CalculateCrc32(crcInput);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint CalculateCrc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }

    #endregion

    #region Binary Helpers

    private static ushort ReadUInt16(byte[] data, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] data, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static void WriteUInt16(byte[] data, int offset, ushort value, bool littleEndian)
    {
        if (littleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value, bool littleEndian)
    {
        if (littleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
    }

    #endregion

    #region Magic Byte Detection

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

    private static bool IsStandaloneJpegMarker(byte marker) =>
        marker is 0x01 or 0xD0 or 0xD1 or 0xD2 or 0xD3 or 0xD4 or 0xD5 or 0xD6 or 0xD7;

    private static bool StartsWithAscii(byte[] source, int offset, int availableLength, string prefix)
    {
        var prefixBytes = Encoding.ASCII.GetBytes(prefix);
        if (availableLength < prefixBytes.Length) return false;
        for (var i = 0; i < prefixBytes.Length; i++)
        {
            if (source[offset + i] != prefixBytes[i]) return false;
        }
        return true;
    }

    private static bool MatchesKnownMagic(byte[]? magicBytes) =>
        MatchesJpegMagic(magicBytes) || MatchesPngMagic(magicBytes);

    private static bool MatchesJpegMagic(byte[]? magicBytes) =>
        magicBytes is { Length: >= 3 } && magicBytes[0] == 0xFF && magicBytes[1] == 0xD8 && magicBytes[2] == 0xFF;

    private static bool MatchesPngMagic(byte[]? bytes) =>
        bytes is { Length: >= 8 } &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    #endregion
}
