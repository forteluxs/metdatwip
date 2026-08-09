using Metawipe.Core.Abstractions;
using Metawipe.Core.Models;

namespace Metawipe.Core.Classification;

/// <summary>
/// Default rule-based classifier for metadata sensitivity.
/// </summary>
public sealed class RuleBasedSensitivityClassifier : ISensitivityClassifier
{
    private static readonly string[] SensitiveTerms =
    [
        "gps", "latitude", "longitude", "author", "creator", "owner", "artist",
        "email", "phone", "serial", "device", "address", "person", "name", "software",
    ];

    /// <inheritdoc />
    public bool IsSensitive(MetadataField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.IsSensitive)
        {
            return true;
        }

        var candidate = $"{field.Group}:{field.Name}:{field.Value}".ToLowerInvariant();
        return SensitiveTerms.Any(candidate.Contains);
    }
}
