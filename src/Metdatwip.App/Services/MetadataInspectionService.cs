using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Routing;

namespace Metdatwip.App.Services;

public sealed class MetadataInspectionService
{
    private readonly FormatRouter _router;

    public MetadataInspectionService(FormatRouter? router = null)
    {
        _router = router ?? FormatRouter.CreateDefault();
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

        var supported = string.Join(", ", _router.GetSupportedReaderExtensions());
        throw new InvalidOperationException(
            $"No supported files found. Supported formats: {supported}.");
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
        var route = _router.ResolveReader(filePath, leadingBytes);

        if (route.IsSupported && route.Handler is not null)
        {
            return route.Handler;
        }

        var supported = string.Join(", ", _router.GetSupportedReaderExtensions());
        throw new InvalidOperationException(
            $"Unsupported file format for '{Path.GetFileName(filePath)}'. Supported formats: {supported}.");
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
            var leadingBytes = ReadLeadingBytes(filePath, 16);
            var route = _router.ResolveReader(filePath, leadingBytes);
            if (route.IsSupported && route.Handler is not null)
            {
                reader = route.Handler;
                return true;
            }

            reader = null;
            return false;
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
