using Metdatwip.Core.Abstractions;

namespace Metdatwip.Core.Routing;

/// <summary>
/// Routes files to registered readers, scrubbers, and writers using extension and optional magic bytes.
/// </summary>
public sealed class FormatRouter
{
    private readonly List<FormatHandlerRegistration<IMetadataReader>> _readerRegistrations = [];
    private readonly List<FormatHandlerRegistration<IMetadataScrubber>> _scrubberRegistrations = [];
    private readonly List<FormatHandlerRegistration<IMetadataWriter>> _writerRegistrations = [];

    /// <summary>
    /// Registers a metadata reader for one logical format.
    /// </summary>
    public void RegisterReader(FormatHandlerRegistration<IMetadataReader> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _readerRegistrations.Add(registration);
    }

    /// <summary>
    /// Registers a metadata scrubber for one logical format.
    /// </summary>
    public void RegisterScrubber(FormatHandlerRegistration<IMetadataScrubber> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _scrubberRegistrations.Add(registration);
    }

    /// <summary>
    /// Resolves a reader by extension and optional magic bytes.
    /// </summary>
    public FormatRouteResult<IMetadataReader> ResolveReader(string filePath, byte[]? magicBytes = null) =>
        Resolve(filePath, magicBytes, _readerRegistrations, "reader");

    /// <summary>
    /// Resolves a scrubber by extension and optional magic bytes.
    /// </summary>
    public FormatRouteResult<IMetadataScrubber> ResolveScrubber(string filePath, byte[]? magicBytes = null) =>
        Resolve(filePath, magicBytes, _scrubberRegistrations, "scrubber");

    /// <summary>
    /// Registers a metadata writer for one logical format.
    /// </summary>
    public void RegisterWriter(FormatHandlerRegistration<IMetadataWriter> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _writerRegistrations.Add(registration);
    }

    /// <summary>
    /// Resolves a writer by extension and optional magic bytes.
    /// </summary>
    public FormatRouteResult<IMetadataWriter> ResolveWriter(string filePath, byte[]? magicBytes = null) =>
        Resolve(filePath, magicBytes, _writerRegistrations, "writer");

    private static FormatRouteResult<THandler> Resolve<THandler>(
        string filePath,
        byte[]? magicBytes,
        IReadOnlyList<FormatHandlerRegistration<THandler>> registrations,
        string handlerKind)
        where THandler : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (registrations.Count == 0)
        {
            return FormatRouteResult<THandler>.Unsupported(
                $"No {handlerKind} registrations were configured.");
        }

        var extension = Path.GetExtension(filePath);

        var magicMatch = registrations.FirstOrDefault(registration =>
            registration.MatchesMagicBytes(magicBytes));
        if (magicMatch is not null)
        {
            return FormatRouteResult<THandler>.Supported(
                magicMatch.FormatName,
                magicMatch.Handler,
                $"Matched {handlerKind} '{magicMatch.FormatName}' via magic bytes.");
        }

        var extensionMatch = registrations.FirstOrDefault(registration =>
            registration.MatchesExtension(extension));
        if (extensionMatch is not null)
        {
            return FormatRouteResult<THandler>.Supported(
                extensionMatch.FormatName,
                extensionMatch.Handler,
                $"Matched {handlerKind} '{extensionMatch.FormatName}' via extension '{extension}'.");
        }

        var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? "(none)" : extension;
        return FormatRouteResult<THandler>.Unsupported(
            $"Unsupported file format for extension '{normalizedExtension}'.");
    }
}
