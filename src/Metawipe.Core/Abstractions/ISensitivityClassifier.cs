using Metawipe.Core.Models;

namespace Metawipe.Core.Abstractions;

/// <summary>
/// Classifies metadata fields as sensitive or non-sensitive.
/// </summary>
public interface ISensitivityClassifier
{
    /// <summary>
    /// Returns <see langword="true"/> when a metadata field should be treated as sensitive.
    /// </summary>
    /// <param name="field">Field to classify.</param>
    bool IsSensitive(MetadataField field);
}
