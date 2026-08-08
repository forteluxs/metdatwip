using System.Collections.ObjectModel;

namespace Metawipe.Core.Models;

/// <summary>
/// Represents a normalized metadata payload extracted from a source file.
/// Field insertion order is preserved to keep reports deterministic.
/// </summary>
public sealed class MetadataDocument
{
    private readonly List<MetadataField> _fields = [];

    /// <summary>
    /// Initializes a new metadata document.
    /// </summary>
    /// <param name="sourcePath">Path of the source file from which metadata was read.</param>
    /// <param name="fields">Optional initial field set.</param>
    public MetadataDocument(string sourcePath, IEnumerable<MetadataField>? fields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        SourcePath = sourcePath;

        if (fields is not null)
        {
            _fields.AddRange(fields);
        }
    }

    /// <summary>
    /// Gets the source file path associated with this metadata payload.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Gets the ordered list of extracted metadata fields.
    /// </summary>
    public IReadOnlyList<MetadataField> Fields => new ReadOnlyCollection<MetadataField>(_fields);

    /// <summary>
    /// Gets fields grouped by <see cref="MetadataField.Group"/> while preserving original field order.
    /// </summary>
    public IEnumerable<IGrouping<string, MetadataField>> GroupedFields =>
        _fields.GroupBy(field => field.Group, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a metadata field to the document.
    /// </summary>
    /// <param name="field">Field to add.</param>
    public void AddField(MetadataField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _fields.Add(field);
    }
}
