namespace Metdatwip.Core.Models;

/// <summary>
/// Describes the outcome of a scrub operation.
/// </summary>
/// <param name="InputPath">Path of the original file.</param>
/// <param name="OutputPath">Path of the scrubbed output file.</param>
/// <param name="RemovedFields">Number of metadata fields removed.</param>
/// <param name="KeptFields">Number of metadata fields retained.</param>
/// <param name="IsSuccess">Indicates whether the scrub operation completed successfully.</param>
/// <param name="Message">Optional detail about the operation result.</param>
public sealed record ScrubResult(
    string InputPath,
    string OutputPath,
    int RemovedFields,
    int KeptFields,
    bool IsSuccess,
    string? Message = null);
