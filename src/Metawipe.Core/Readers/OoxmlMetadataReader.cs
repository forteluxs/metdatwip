using System.IO.Packaging;
using System.Xml.Linq;
using Metawipe.Core.Abstractions;
using Metawipe.Core.Models;

namespace Metawipe.Core.Readers;

/// <summary>
/// Reads metadata from OOXML packages (DOCX/XLSX/PPTX) by inspecting docProps parts.
/// </summary>
public sealed class OoxmlMetadataReader : IMetadataReader
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

    private readonly ISensitivityClassifier _sensitivityClassifier;

    public OoxmlMetadataReader(ISensitivityClassifier sensitivityClassifier)
    {
        _sensitivityClassifier = sensitivityClassifier ?? throw new ArgumentNullException(nameof(sensitivityClassifier));
    }

    /// <inheritdoc />
    public string Name => "ooxml-metadata-reader";

    /// <inheritdoc />
    public bool CanRead(string filePath, byte[]? magicBytes = null)
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
    public Task<MetadataDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found.", filePath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var package = Package.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = new MetadataDocument(filePath);

        ReadCoreProperties(package, document, cancellationToken);
        ReadAppProperties(package, document, cancellationToken);
        ReadCustomProperties(package, document, cancellationToken);

        return Task.FromResult(document);
    }

    private void ReadCoreProperties(Package package, MetadataDocument document, CancellationToken cancellationToken)
    {
        if (!TryGetPart(package, CorePartPath, out var part))
        {
            return;
        }

        var xdoc = LoadPartXDocument(part);
        var root = xdoc.Root;
        if (root is null)
        {
            return;
        }

        foreach (var element in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = NormalizeValue(element.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            AddField(document, "OOXML-Core", element.Name.LocalName, value);
        }
    }

    private void ReadAppProperties(Package package, MetadataDocument document, CancellationToken cancellationToken)
    {
        if (!TryGetPart(package, AppPartPath, out var part))
        {
            return;
        }

        var xdoc = LoadPartXDocument(part);
        var root = xdoc.Root;
        if (root is null)
        {
            return;
        }

        foreach (var element in root.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = NormalizeValue(element.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            AddField(document, "OOXML-App", element.Name.LocalName, value);
        }
    }

    private void ReadCustomProperties(Package package, MetadataDocument document, CancellationToken cancellationToken)
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

        foreach (var propertyElement in root.Elements().Where(element =>
                     string.Equals(element.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var propertyName = propertyElement.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                propertyName = propertyElement.Name.LocalName;
            }

            var value = ReadCustomPropertyValue(propertyElement);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            AddField(document, "OOXML-Custom", propertyName, value);
        }
    }

    private void AddField(MetadataDocument document, string group, string name, string value)
    {
        var tentative = new MetadataField(group, name, value, false, true);
        var isSensitive = _sensitivityClassifier.IsSensitive(tentative);
        document.AddField(tentative with { IsSensitive = isSensitive });
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
