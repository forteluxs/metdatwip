using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;

namespace Metdatwip.Core.Readers;

/// <summary>
/// Reads metadata from supported image formats and normalizes fields into metdatwip groups.
/// </summary>
public sealed class ImageMetadataReader : IMetadataReader
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".tif",
        ".tiff",
        ".heic",
        ".heif",
        ".webp",
    ];

    private readonly ISensitivityClassifier _sensitivityClassifier;

    public ImageMetadataReader(ISensitivityClassifier sensitivityClassifier)
    {
        _sensitivityClassifier = sensitivityClassifier ?? throw new ArgumentNullException(nameof(sensitivityClassifier));
    }

    /// <inheritdoc />
    public string Name => "image-metadata-reader";

    /// <inheritdoc />
    public bool CanRead(string filePath, byte[]? magicBytes = null)
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
    public Task<MetadataDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found.", filePath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var metadataDirectories = MetadataExtractor.ImageMetadataReader.ReadMetadata(filePath);
        var document = new MetadataDocument(filePath);

        foreach (var directory in metadataDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var group = MapGroup(directory);
            if (group is null)
            {
                continue;
            }

            foreach (var tag in directory.Tags)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var description = tag.Description;
                if (string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                var tentative = new MetadataField(group, tag.Name, description, false, true);
                var isSensitive = _sensitivityClassifier.IsSensitive(tentative);
                document.AddField(tentative with { IsSensitive = isSensitive });
            }
        }

        return Task.FromResult(document);
    }

    private static string? MapGroup(MetadataExtractor.Directory directory) => directory switch
    {
        GpsDirectory => "GPS",
        XmpDirectory => "XMP",
        IptcDirectory => "IPTC",
        IccDirectory => "ICC",
        ExifDirectoryBase => "EXIF",
        _ => null,
    };

    private static bool MatchesKnownMagic(byte[]? magicBytes)
    {
        if (magicBytes is null || magicBytes.Length < 3)
        {
            return false;
        }

        // JPEG
        if (magicBytes.Length >= 3 &&
            magicBytes[0] == 0xFF &&
            magicBytes[1] == 0xD8 &&
            magicBytes[2] == 0xFF)
        {
            return true;
        }

        // PNG
        if (magicBytes.Length >= 8 &&
            magicBytes[0] == 0x89 && magicBytes[1] == 0x50 && magicBytes[2] == 0x4E && magicBytes[3] == 0x47 &&
            magicBytes[4] == 0x0D && magicBytes[5] == 0x0A && magicBytes[6] == 0x1A && magicBytes[7] == 0x0A)
        {
            return true;
        }

        // TIFF (little- or big-endian)
        if (magicBytes.Length >= 4 &&
            ((magicBytes[0] == 0x49 && magicBytes[1] == 0x49 && magicBytes[2] == 0x2A && magicBytes[3] == 0x00) ||
             (magicBytes[0] == 0x4D && magicBytes[1] == 0x4D && magicBytes[2] == 0x00 && magicBytes[3] == 0x2A)))
        {
            return true;
        }

        // WEBP container: RIFF....WEBP
        if (magicBytes.Length >= 12 &&
            magicBytes[0] == (byte)'R' && magicBytes[1] == (byte)'I' && magicBytes[2] == (byte)'F' && magicBytes[3] == (byte)'F' &&
            magicBytes[8] == (byte)'W' && magicBytes[9] == (byte)'E' && magicBytes[10] == (byte)'B' && magicBytes[11] == (byte)'P')
        {
            return true;
        }

        // HEIF/HEIC ISO BMFF: ....ftypheic/heif/heix/hevc/hevx/mif1/msf1
        if (magicBytes.Length >= 12 &&
            magicBytes[4] == (byte)'f' && magicBytes[5] == (byte)'t' && magicBytes[6] == (byte)'y' && magicBytes[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(magicBytes, 8, 4);
            return brand is "heic" or "heif" or "heix" or "hevc" or "hevx" or "mif1" or "msf1";
        }

        return false;
    }
}
