using System.IO.Packaging;
using System.Xml.Linq;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Writers;

/// <summary>
/// Writes metadata edits to OOXML package files (DOCX, XLSX, PPTX)
/// by modifying XML values inside docProps/core.xml, docProps/app.xml, and docProps/custom.xml.
/// </summary>
public sealed class OoxmlMetadataWriter : IMetadataWriter
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

    public OoxmlMetadataWriter(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new OoxmlMetadataReader(sensitivityClassifier);
    }

    /// <inheritdoc />
    public string Name => "ooxml-metadata-writer";

    /// <inheritdoc />
    public bool CanWrite(string filePath, byte[]? magicBytes = null)
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
            throw new NotSupportedException("Unsupported OOXML format. Supported: DOCX, XLSX, PPTX.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        File.Copy(inputPath, targetFile, overwrite: true);

        var appliedEdits = 0;
        var skippedEdits = 0;

        using (var package = Package.Open(targetFile, FileMode.Open, FileAccess.ReadWrite))
        {
            foreach (var edit in edits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var groupLower = edit.Group.ToLowerInvariant();

                var applied = groupLower switch
                {
                    "ooxml-core" => ApplySimplePartEdit(package, CorePartPath, edit),
                    "ooxml-app" => ApplySimplePartEdit(package, AppPartPath, edit),
                    "ooxml-custom" => ApplyCustomPartEdit(package, edit),
                    _ => false,
                };

                if (applied)
                {
                    appliedEdits++;
                }
                else
                {
                    skippedEdits++;
                }
            }
        }

        if (isSameFile)
        {
            File.Move(targetFile, outputPath, overwrite: true);
        }

        // Verify by re-reading the edited file
        var afterDocument = await _reader.ReadAsync(outputPath, cancellationToken);
        var verifiedEdits = 0;
        foreach (var edit in edits)
        {
            var matchingField = afterDocument.Fields.FirstOrDefault(f =>
                string.Equals(f.Group, edit.Group, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Name, edit.Name, StringComparison.OrdinalIgnoreCase));

            if (matchingField is not null && string.Equals(matchingField.Value, edit.NewValue.Trim()))
            {
                verifiedEdits++;
            }
        }

        var message = $"Applied {appliedEdits} edit(s), skipped {skippedEdits}. " +
                      $"Verification: {verifiedEdits}/{appliedEdits} edits confirmed.";

        return new WriteResult(inputPath, outputPath, appliedEdits, skippedEdits, true, message);
    }

    private static bool ApplySimplePartEdit(Package package, string partPath, MetadataEdit edit)
    {
        if (!TryGetPart(package, partPath, out var part))
        {
            return false;
        }

        var xdoc = LoadPartXDocument(part);
        var root = xdoc.Root;
        if (root is null)
        {
            return false;
        }

        foreach (var element in root.Elements())
        {
            if (string.Equals(element.Name.LocalName, edit.Name, StringComparison.OrdinalIgnoreCase))
            {
                element.Value = edit.NewValue;
                SavePartXDocument(part, xdoc);
                return true;
            }
        }

        return false;
    }

    private static bool ApplyCustomPartEdit(Package package, MetadataEdit edit)
    {
        if (!TryGetPart(package, CustomPartPath, out var part))
        {
            return false;
        }

        var xdoc = LoadPartXDocument(part);
        var root = xdoc.Root;
        if (root is null)
        {
            return false;
        }

        foreach (var propertyElement in root.Elements().Where(element =>
                     string.Equals(element.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase)))
        {
            var propertyName = propertyElement.Attribute("name")?.Value;
            if (!string.Equals(propertyName, edit.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Set value in the first descendant value element
            var valueDescendant = propertyElement.Descendants().FirstOrDefault();
            if (valueDescendant is not null)
            {
                valueDescendant.Value = edit.NewValue;
            }
            else
            {
                propertyElement.Value = edit.NewValue;
            }

            SavePartXDocument(part, xdoc);
            return true;
        }

        return false;
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
