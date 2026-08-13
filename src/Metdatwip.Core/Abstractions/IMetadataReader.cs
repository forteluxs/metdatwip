using Metdatwip.Core.Models;

namespace Metdatwip.Core.Abstractions;

/// <summary>
/// Reads file metadata and returns a normalized <see cref="MetadataDocument"/>.
/// </summary>
public interface IMetadataReader
{
    /// <summary>
    /// Gets a friendly handler name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines whether this reader can process the file based on extension and optional magic bytes.
    /// </summary>
    /// <param name="filePath">Path of the candidate file.</param>
    /// <param name="magicBytes">Optional leading bytes from the file.</param>
    bool CanRead(string filePath, byte[]? magicBytes = null);

    /// <summary>
    /// Reads metadata from the specified file.
    /// </summary>
    /// <param name="filePath">Path to the source file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MetadataDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
