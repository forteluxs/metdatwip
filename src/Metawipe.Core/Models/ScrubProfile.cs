namespace Metawipe.Core.Models;

/// <summary>
/// Defines metadata retention/removal rules used by scrubbers.
/// </summary>
public sealed class ScrubProfile
{
    private readonly HashSet<string> _whitelist;

    private ScrubProfile(ScrubProfileMode mode, IEnumerable<string>? whitelist)
    {
        Mode = mode;
        _whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (whitelist is null)
        {
            return;
        }

        foreach (var fieldKey in whitelist)
        {
            if (string.IsNullOrWhiteSpace(fieldKey))
            {
                continue;
            }

            _whitelist.Add(NormalizeFieldKey(fieldKey));
        }
    }

    /// <summary>
    /// Gets the profile mode.
    /// </summary>
    public ScrubProfileMode Mode { get; }

    /// <summary>
    /// Gets the normalized whitelist field keys used when <see cref="Mode"/> is
    /// <see cref="ScrubProfileMode.KeepWhitelist"/>.
    /// </summary>
    public IReadOnlyCollection<string> Whitelist => _whitelist;

    /// <summary>
    /// Creates a profile that removes all removable fields.
    /// </summary>
    public static ScrubProfile CreateStripAll() => new(ScrubProfileMode.StripAll, null);

    /// <summary>
    /// Creates a profile that keeps only the provided field keys.
    /// </summary>
    /// <param name="fieldKeys">Field keys in <c>group/name</c> format.</param>
    public static ScrubProfile CreateKeepWhitelist(IEnumerable<string> fieldKeys)
    {
        ArgumentNullException.ThrowIfNull(fieldKeys);
        return new ScrubProfile(ScrubProfileMode.KeepWhitelist, fieldKeys);
    }

    /// <summary>
    /// Builds a normalized key in <c>group/name</c> format for whitelist comparisons.
    /// </summary>
    public static string CreateFieldKey(string group, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return NormalizeFieldKey($"{group}/{name}");
    }

    /// <summary>
    /// Returns <see langword="true"/> when a metadata field should be removed.
    /// Non-removable fields are always retained.
    /// </summary>
    /// <param name="field">Metadata field to evaluate.</param>
    public bool ShouldRemove(MetadataField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (!field.Removable)
        {
            return false;
        }

        return Mode switch
        {
            ScrubProfileMode.StripAll => true,
            ScrubProfileMode.KeepWhitelist =>
                !_whitelist.Contains(CreateFieldKey(field.Group, field.Name)),
            _ => true,
        };
    }

    private static string NormalizeFieldKey(string fieldKey) =>
        fieldKey.Trim().ToLowerInvariant();
}
