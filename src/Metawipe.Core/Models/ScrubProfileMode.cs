namespace Metawipe.Core.Models;

/// <summary>
/// Controls how metadata removal should be applied.
/// </summary>
public enum ScrubProfileMode
{
    /// <summary>
    /// Remove every removable metadata field.
    /// </summary>
    StripAll = 0,

    /// <summary>
    /// Keep only whitelisted fields and remove all other removable fields.
    /// </summary>
    KeepWhitelist = 1,
}
