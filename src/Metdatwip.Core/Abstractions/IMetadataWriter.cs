using Metdatwip.Core.Models;

namespace Metdatwip.Core.Abstractions;

/// <summary>
/// Writes or modifies metadata fields in files.
/// </summary>
public interface IMetadataWriter
{
    /// <summary>
    /// Gets a friendly handler name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines whether this writer can process the file based on extension and optional magic bytes.
    /// </summary>
    /// <param name="filePath">Path of the candidate file.</param>
    /// <param name="magicBytes">Optional leading bytes from the file.</param>
    bool CanWrite(string filePath, byte[]? magicBytes = null);

    /// <summary>
    /// Writes metadata edits to <paramref name="outputPath"/> based on the provided <paramref name="edits"/>.
    /// The original file at <paramref name="inputPath"/> is never modified.
    /// </summary>
    /// <param name="inputPath">Path to the original file.</param>
    /// <param name="outputPath">Destination path for the edited output.</param>
    /// <param name="edits">The set of field edits to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WriteResult> WriteAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<MetadataEdit> edits,
        CancellationToken cancellationToken = default);
}
