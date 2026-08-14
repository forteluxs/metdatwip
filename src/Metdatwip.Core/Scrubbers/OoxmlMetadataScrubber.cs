using System.IO.Packaging;
using System.Xml.Linq;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Scrubbers;

/// <summary>
/// Scrubs OOXML package metadata from docProps/core.xml, docProps/app.xml, and docProps/custom.xml.
/// </summary>
public sealed class OoxmlMetadataScrubber : IMetadataScrubber
{
    private const string CorePartPath = "docProps/core.xml";
    private const string AppPartPath = "docProps/app.xml";
    private const string CustomPartPath = "docProps/custom.xml";

    private static readonly HashSet<string> SupportedExtensions =
    [
        ".docx",
        ".xlsx",
        ".pptx",
    ];

    private readonly OoxmlMetadataReader _reader;

    public OoxmlMetadataScrubber(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new OoxmlMetadataReader(sensitivityClassifier);
    }

    /// <inheritdoc />
    public string Name => "ooxml-metadata-scrubber";

    /// <inheritdoc />
    public bool CanScrub(string filePath, byte[]? magicBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension.ToLowerInvariant()))
        {
            return true;
        }

        return MatchesZipMagic(magicBytes);
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
        var isSameFile = string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        var targetFile = isSameFile ? Path.Combine(Path.GetTempPath(), "metdatwip_scrub_ooxml_" + Guid.NewGuid().ToString("N") + Path.GetExtension(inputPath)) : outputFullPath;

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException("Unsupported OOXML format. Supported: DOCX, XLSX, PPTX.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var beforeDocument = await _reader.ReadAsync(inputPath, cancellationToken);

        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        File.Copy(inputPath, targetFile, overwrite: true);

        using (var package = Package.Open(targetFile, FileMode.Open, FileAccess.ReadWrite))
        {
            ApplyProfile(package, profile, cancellationToken);
        }

        if (isSameFile)
        {
            File.Move(targetFile, outputFullPath, overwrite: true);
        }

        var afterDocument = await _reader.ReadAsync(outputFullPath, cancellationToken);
        var removedFields = Math.Max(0, beforeDocument.Fields.Count - afterDocument.Fields.Count);
        var keptFields = afterDocument.Fields.Count;
        var sensitiveRemaining = afterDocument.Fields.Count(field => field.IsSensitive);

        var message = sensitiveRemaining == 0
            ? "Verification scan: 0 sensitive fields remaining."
            : $"Verification scan: {sensitiveRemaining} sensitive field(s) remaining.";

        return new ScrubResult(inputPath, outputFullPath, removedFields, keptFields, true, message);
    }

    private static void ApplyProfile(Package package, ScrubProfile profile, CancellationToken cancellationToken)
    {
        ApplySimplePart(profile, package, CorePartPath, "OOXML-Core", cancellationToken);
        ApplySimplePart(profile, package, AppPartPath, "OOXML-App", cancellationToken);
        ApplyCustomPart(profile, package, cancellationToken);
    }

    private static void ApplySimplePart(
        ScrubProfile profile,
        Package package,
        string partPath,
        string group,
        CancellationToken cancellationToken)
    {
        if (!TryGetPart(package, partPath, out var part))
        {
            return;
        }

        var xdoc = LoadPartXDocument(part);
        var root = xdoc.Root;
        if (root is null)
        {
            return;
        }

        var changed = false;
        foreach (var element in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = NormalizeValue(element.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var field = new MetadataField(group, element.Name.LocalName, value, false, true);
            if (!profile.ShouldRemove(field))
            {
                continue;
            }

            element.Value = string.Empty;
            changed = true;
        }

        if (changed)
        {
            SavePartXDocument(part, xdoc);
        }
    }

    private static void ApplyCustomPart(ScrubProfile profile, Package package, CancellationToken cancellationToken)
    {
        if (!TryGetPart(package, CustomPartPath, out var part))
        {
            return;
        }

        var xdoc = LoadPartXDocument(part);
        var root = xdoc.Root;
        if (root is null)
        {
            return;
        }

        var changed = false;
        foreach (var propertyElement in root.Elements().Where(element =>
                     string.Equals(element.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = ReadCustomPropertyValue(propertyElement);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var propertyName = propertyElement.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                propertyName = propertyElement.Name.LocalName;
            }

            var field = new MetadataField("OOXML-Custom", propertyName, value, false, true);
            if (!profile.ShouldRemove(field))
            {
                continue;
            }

            propertyElement.Remove();
            changed = true;
        }

        if (changed)
        {
            SavePartXDocument(part, xdoc);
        }
    }

    private static bool TryGetPart(Package package, string partPath, out PackagePart part)
    {
        var partUri = PackUriHelper.CreatePartUri(new Uri(partPath, UriKind.Relative));
        if (!package.PartExists(partUri))
        {
            part = null!;
            return false;
        }

        part = package.GetPart(partUri);
        return true;
    }

    private static XDocument LoadPartXDocument(PackagePart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static void SavePartXDocument(PackagePart part, XDocument xdoc)
    {
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        xdoc.Save(stream, SaveOptions.DisableFormatting);
    }

    private static string ReadCustomPropertyValue(XElement propertyElement)
    {
        foreach (var descendant in propertyElement.Descendants())
        {
            var value = NormalizeValue(descendant.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string NormalizeValue(string? rawValue) =>
        string.IsNullOrWhiteSpace(rawValue) ? string.Empty : rawValue.Trim();

    private static bool MatchesZipMagic(byte[]? magicBytes)
    {
        if (magicBytes is null || magicBytes.Length < 4)
        {
            return false;
        }

        return magicBytes[0] == (byte)'P' &&
               magicBytes[1] == (byte)'K' &&
               magicBytes[2] is 0x03 or 0x05 or 0x07 &&
               magicBytes[3] is 0x04 or 0x06 or 0x08;
    }
}
