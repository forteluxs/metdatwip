using Metawipe.Core.Abstractions;
using Metawipe.Core.Classification;
using Metawipe.Core.Models;
using Metawipe.Core.Readers;

namespace Metawipe.App.Services;

public sealed class MetadataInspectionService
{
    private static readonly string[] SupportedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".tif",
        ".tiff",
        ".heic",
        ".heif",
        ".webp",
        ".docx",
        ".xlsx",
        ".pptx",
    ];

    private readonly IReadOnlyList<IMetadataReader> _readers;

    public MetadataInspectionService()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _readers =
        [
            new ImageMetadataReader(classifier),
            new OoxmlMetadataReader(classifier),
        ];
    }

    public async Task<InspectionResult> InspectAsync(IEnumerable<string> droppedPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(droppedPaths);

        foreach (var path in droppedPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
                return await InspectFileAsync(Path.GetFullPath(path), wasFolderInput: false, cancellationToken);
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            var candidateFile = FindFirstSupportedFile(path, cancellationToken);
            if (candidateFile is null)
            {
                continue;
            }

            var note = $"Folder drop detected: inspecting first supported file from '{Path.GetFileName(path)}'.";
            return await InspectFileAsync(candidateFile, wasFolderInput: true, cancellationToken, note);
        }

        throw new InvalidOperationException(
            $"No supported files found. Supported formats: {string.Join(", ", SupportedExtensions)}.");
    }

    private async Task<InspectionResult> InspectFileAsync(
        string filePath,
        bool wasFolderInput,
        CancellationToken cancellationToken,
        string? note = null)
    {
        var reader = ResolveReader(filePath);
        var document = await reader.ReadAsync(filePath, cancellationToken);
        return new InspectionResult(filePath, document, wasFolderInput, note);
    }

    private IMetadataReader ResolveReader(string filePath)
    {
        var leadingBytes = ReadLeadingBytes(filePath, 16);
        var reader = _readers.FirstOrDefault(candidate => candidate.CanRead(filePath, leadingBytes));

        return reader ?? throw new InvalidOperationException(
            $"Unsupported file format for '{Path.GetFileName(filePath)}'. Supported formats: {string.Join(", ", SupportedExtensions)}.");
    }

    private string? FindFirstSupportedFile(string folderPath, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryResolveReader(filePath, out _))
                {
                    return Path.GetFullPath(filePath);
                }
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryResolveReader(filePath, out _))
                {
                    return Path.GetFullPath(filePath);
                }
            }

            return null;
        }
    }

    private bool TryResolveReader(string filePath, out IMetadataReader? reader)
    {
        try
        {
            reader = ResolveReader(filePath);
            return true;
        }
        catch
        {
            reader = null;
            return false;
        }
    }

    private static byte[] ReadLeadingBytes(string filePath, int count)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[count];
        var bytesRead = stream.Read(buffer, 0, count);

        if (bytesRead == count)
        {
            return buffer;
        }

        return buffer.Take(bytesRead).ToArray();
    }
}

public sealed record InspectionResult(
    string SourcePath,
    MetadataDocument Document,
    bool WasFolderInput,
    string? Note);
