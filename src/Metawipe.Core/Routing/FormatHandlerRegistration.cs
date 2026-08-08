namespace Metawipe.Core.Routing;

/// <summary>
/// Binds a handler to one logical format with extension and optional magic-byte matching.
/// </summary>
/// <typeparam name="THandler">Handler type to register.</typeparam>
public sealed class FormatHandlerRegistration<THandler>
    where THandler : class
{
    private readonly HashSet<string> _extensions;

    /// <summary>
    /// Initializes a registration for one file format.
    /// </summary>
    /// <param name="formatName">Friendly format name (for example JPEG, PNG, PDF).</param>
    /// <param name="handler">Reader or scrubber handler instance.</param>
    /// <param name="extensions">Supported file extensions.</param>
    /// <param name="magicBytesMatcher">Optional magic-byte matcher.</param>
    public FormatHandlerRegistration(
        string formatName,
        THandler handler,
        IEnumerable<string> extensions,
        Func<byte[], bool>? magicBytesMatcher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatName);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(extensions);

        FormatName = formatName;
        Handler = handler;
        MagicBytesMatcher = magicBytesMatcher;
        _extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            _extensions.Add(NormalizeExtension(extension));
        }

        if (_extensions.Count == 0 && magicBytesMatcher is null)
        {
            throw new ArgumentException(
                "At least one extension or a magic-byte matcher is required.",
                nameof(extensions));
        }
    }

    /// <summary>
    /// Gets the friendly format name.
    /// </summary>
    public string FormatName { get; }

    /// <summary>
    /// Gets the registered handler.
    /// </summary>
    public THandler Handler { get; }

    /// <summary>
    /// Gets the extension set used by this registration.
    /// </summary>
    public IReadOnlyCollection<string> Extensions => _extensions;

    /// <summary>
    /// Gets the optional magic-byte matcher.
    /// </summary>
    public Func<byte[], bool>? MagicBytesMatcher { get; }

    /// <summary>
    /// Determines whether this registration matches the given extension.
    /// </summary>
    public bool MatchesExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return _extensions.Contains(NormalizeExtension(extension));
    }

    /// <summary>
    /// Determines whether this registration matches the provided magic bytes.
    /// </summary>
    public bool MatchesMagicBytes(byte[]? magicBytes)
    {
        if (magicBytes is null || magicBytes.Length == 0 || MagicBytesMatcher is null)
        {
            return false;
        }

        return MagicBytesMatcher(magicBytes);
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim();
        if (!normalized.StartsWith('.'))
        {
            normalized = $".{normalized}";
        }

        return normalized.ToLowerInvariant();
    }
}
