namespace Metdatwip.Core.Models;

/// <summary>
/// Represents a single metadata field edit: the target field key and the new value to set.
/// </summary>
/// <param name="Group">Logical category for the field (for example EXIF, XMP, OOXML-Core).</param>
/// <param name="Name">Field name within the group.</param>
/// <param name="NewValue">The new value to write. Use empty string to clear the field without removing it.</param>
public sealed record MetadataEdit(
    string Group,
    string Name,
    string NewValue)
{
    /// <summary>
    /// Creates the normalized field key in "group/name" format.
    /// </summary>
    public string FieldKey => ScrubProfile.CreateFieldKey(Group, Name);

    /// <summary>
    /// Parses a "group/name=value" string into a <see cref="MetadataEdit"/>.
    /// </summary>
    /// <param name="input">Input string in the format "group/name=value".</param>
    /// <returns>A parsed <see cref="MetadataEdit"/> instance.</returns>
    /// <exception cref="FormatException">Thrown when the input format is invalid.</exception>
    public static MetadataEdit Parse(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var equalsIndex = input.IndexOf('=');
        if (equalsIndex < 0)
        {
            throw new FormatException(
                $"Invalid edit format: '{input}'. Expected 'group/name=value'.");
        }

        var key = input[..equalsIndex].Trim();
        var value = input[(equalsIndex + 1)..];

        var slashIndex = key.IndexOf('/');
        if (slashIndex < 0)
        {
            throw new FormatException(
                $"Invalid field key: '{key}'. Expected 'group/name' format (e.g., 'exif/artist=John').");
        }

        var group = key[..slashIndex].Trim();
        var name = key[(slashIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException(
                $"Invalid field key: '{key}'. Both group and name must be non-empty.");
        }

        return new MetadataEdit(group, name, value);
    }
}
