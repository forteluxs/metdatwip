using System.Text.RegularExpressions;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;

namespace Metdatwip.Core.Classification;

/// <summary>
/// Rule-based and pattern-based classifier for metadata sensitivity.
/// Evaluates field groups, tag names, and content values for PII, location, device IDs, and fingerprints.
/// </summary>
public sealed class RuleBasedSensitivityClassifier : ISensitivityClassifier
{
    private static readonly HashSet<string> AlwaysSensitiveGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "GPS",
        "Location",
        "Geotag",
    };

    private static readonly string[] SensitiveFieldNameTerms =
    [
        "gps", "latitude", "longitude", "altitude", "position", "coord", "geo", "location",
        "author", "creator", "artist", "owner", "lastmodifiedby", "contributor", "publisher",
        "copyright", "byline", "credit", "contact", "person", "composer", "director", "performer",
        "client", "customer", "editor", "user", "name",
        "email", "mail", "phone", "mobile", "tel", "fax", "address", "street", "city", "state", "postal", "zip", "country",
        "serial", "device", "camera", "lens", "uniqueid", "uuid", "guid", "mac", "hostname", "computer", "machine",
        "software", "tool", "encoder", "producer", "application", "app", "firmware", "make", "model",
        "history", "revision",
    ];

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PhoneRegex = new(
        @"(?:\+?\d{1,3}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Ipv4Regex = new(
        @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GpsCoordinatesRegex = new(
        @"[-+]?([1-8]?\d(\.\d+)?|90(\.0+)?)\s*,\s*[-+]?(180(\.0+)?|((1[0-7]\d)|([1-9]?\d))(\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UuidRegex = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MacAddressRegex = new(
        @"\b(?:[0-9A-Fa-f]{2}[:-]){5}(?:[0-9A-Fa-f]{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UserPathRegex = new(
        @"(?:[A-Za-z]:\\Users\\[^\\]+|/(?:home|Users)/[^/\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public bool IsSensitive(MetadataField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.IsSensitive)
        {
            return true;
        }

        // 1. Group checks (e.g. any tag in GPS group is sensitive)
        if (!string.IsNullOrWhiteSpace(field.Group) && AlwaysSensitiveGroups.Contains(field.Group))
        {
            return true;
        }

        // 2. Field name and group terms checks
        var normalizedName = NormalizeTerm(field.Name);
        var normalizedGroup = NormalizeTerm(field.Group);

        if (SensitiveFieldNameTerms.Any(term => normalizedName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                                normalizedGroup.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 3. Value content pattern analysis (PII, email, phone, IP, GPS, UUID, User paths)
        if (!string.IsNullOrWhiteSpace(field.Value))
        {
            var value = field.Value;

            if (EmailRegex.IsMatch(value)) return true;
            if (UuidRegex.IsMatch(value)) return true;
            if (MacAddressRegex.IsMatch(value)) return true;
            if (UserPathRegex.IsMatch(value)) return true;
            if (Ipv4Regex.IsMatch(value) && !IsLocalLoopback(value)) return true;
            if (GpsCoordinatesRegex.IsMatch(value)) return true;
            if (value.Length >= 7 && PhoneRegex.IsMatch(value)) return true;
        }

        return false;
    }

    private static string NormalizeTerm(string input) =>
        string.IsNullOrWhiteSpace(input) ? string.Empty : input.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();

    private static bool IsLocalLoopback(string value) =>
        value.Contains("127.0.0.1") || value.Contains("0.0.0.0");
}
