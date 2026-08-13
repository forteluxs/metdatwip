using Metdatwip.Core.Models;

namespace Metdatwip.Core.Abstractions;

/// <summary>
/// Scrubs metadata from files according to a <see cref="ScrubProfile"/>.
/// </summary>
public interface IMetadataScrubber
{
    /// <summary>
    /// Gets a friendly handler name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines whether this scrubber can process the file based on extension and optional magic bytes.
    /// </summary>
    /// <param name="filePath">Path of the candidate file.</param>
    /// <param name="magicBytes">Optional leading bytes from the file.</param>
    bool CanScrub(string filePath, byte[]? magicBytes = null);

    /// <summary>
    /// Applies a profile and writes a cleaned copy to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="inputPath">Path to the original file.</param>
    /// <param name="outputPath">Destination path for cleaned output.</param>
    /// <param name="profile">Removal profile to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ScrubResult> ScrubAsync(
        string inputPath,
        string outputPath,
        ScrubProfile profile,
        CancellationToken cancellationToken = default);
}
