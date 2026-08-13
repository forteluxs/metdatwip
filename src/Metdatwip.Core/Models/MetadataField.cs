namespace Metdatwip.Core.Models;

/// <summary>
/// Represents a single metadata field extracted from a file.
/// </summary>
/// <param name="Group">Logical category for the field (for example EXIF, XMP, PDF, OOXML).</param>
/// <param name="Name">Field name within the group.</param>
/// <param name="Value">Raw or normalized field value.</param>
/// <param name="IsSensitive">Indicates whether the field may contain sensitive information.</param>
/// <param name="Removable">Indicates whether the field can be removed by scrubbers.</param>
public sealed record MetadataField(
    string Group,
    string Name,
    string Value,
    bool IsSensitive,
    bool Removable);
