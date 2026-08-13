namespace Metdatwip.Core.Models;

/// <summary>
/// Describes the outcome of a metadata write/edit operation.
/// </summary>
/// <param name="InputPath">Path of the original file.</param>
/// <param name="OutputPath">Path of the edited output file.</param>
/// <param name="AppliedEdits">Number of metadata edits successfully applied.</param>
/// <param name="SkippedEdits">Number of edits that could not be applied (field not found or not supported).</param>
/// <param name="IsSuccess">Indicates whether the write operation completed successfully.</param>
/// <param name="Message">Optional detail about the operation result.</param>
public sealed record WriteResult(
    string InputPath,
    string OutputPath,
    int AppliedEdits,
    int SkippedEdits,
    bool IsSuccess,
    string? Message = null);
