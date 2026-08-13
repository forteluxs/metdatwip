namespace Metdatwip.Core.Routing;

/// <summary>
/// Represents the output of a format routing decision.
/// </summary>
/// <typeparam name="THandler">Handler type being resolved.</typeparam>
public sealed class FormatRouteResult<THandler>
    where THandler : class
{
    private FormatRouteResult(
        bool isSupported,
        string message,
        string? formatName,
        THandler? handler)
    {
        IsSupported = isSupported;
        Message = message;
        FormatName = formatName;
        Handler = handler;
    }

    /// <summary>
    /// Gets whether routing found a supporting handler.
    /// </summary>
    public bool IsSupported { get; }

    /// <summary>
    /// Gets the friendly format name when supported.
    /// </summary>
    public string? FormatName { get; }

    /// <summary>
    /// Gets the resolved handler instance when supported.
    /// </summary>
    public THandler? Handler { get; }

    /// <summary>
    /// Gets a human-readable routing explanation.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a supported route result.
    /// </summary>
    public static FormatRouteResult<THandler> Supported(string formatName, THandler handler, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatName);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new FormatRouteResult<THandler>(true, message, formatName, handler);
    }

    /// <summary>
    /// Creates an unsupported route result with a clear reason.
    /// </summary>
    public static FormatRouteResult<THandler> Unsupported(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new FormatRouteResult<THandler>(false, message, null, null);
    }
}
