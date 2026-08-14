using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Classification;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Scrubbers;
using Metdatwip.Core.Writers;

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
    /// Creates and configures a default FormatRouter instance with all built-in format readers, scrubbers, and writers.
    /// </summary>
    public static FormatRouter CreateDefault(ISensitivityClassifier? classifier = null)
    {
        classifier ??= new RuleBasedSensitivityClassifier();
        var router = new FormatRouter();

        var imageReader = new ImageMetadataReader(classifier);
        var imageScrubber = new ImageMetadataScrubber(classifier);
        var imageWriter = new ImageMetadataWriter(classifier);

        var ooxmlReader = new OoxmlMetadataReader(classifier);
        var ooxmlScrubber = new OoxmlMetadataScrubber(classifier);
        var ooxmlWriter = new OoxmlMetadataWriter(classifier);

        var audioReader = new AudioMetadataReader(classifier);
        var audioScrubber = new AudioMetadataScrubber(classifier);
        var audioWriter = new AudioMetadataWriter(classifier);

        var videoReader = new VideoMetadataReader(classifier);
        var videoScrubber = new VideoMetadataScrubber(classifier);
        var videoWriter = new VideoMetadataWriter(classifier);

        var pdfReader = new PdfMetadataReader(classifier);
        var pdfScrubber = new PdfMetadataScrubber(classifier);
        var pdfWriter = new PdfMetadataWriter(classifier);

        // Images
        router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
            "Image",
            imageReader,
            [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".heic", ".heif", ".webp"],
            MatchesImageMagic));

        router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
            "Image",
            imageScrubber,
            [".jpg", ".jpeg", ".png"],
            MatchesImageMagic));

        router.RegisterWriter(new FormatHandlerRegistration<IMetadataWriter>(
            "Image",
            imageWriter,
            [".jpg", ".jpeg", ".png"],
            MatchesImageMagic));

        // PDF Documents
        router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
            "PDF",
            pdfReader,
            [".pdf"],
            MatchesPdfMagic));

        router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
            "PDF",
            pdfScrubber,
            [".pdf"],
            MatchesPdfMagic));

        router.RegisterWriter(new FormatHandlerRegistration<IMetadataWriter>(
            "PDF",
            pdfWriter,
            [".pdf"],
            MatchesPdfMagic));

        // OOXML Documents
        router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
            "OOXML",
            ooxmlReader,
            [".docx", ".xlsx", ".pptx"],
            MatchesZipMagic));

        router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
            "OOXML",
            ooxmlScrubber,
            [".docx", ".xlsx", ".pptx"],
            MatchesZipMagic));

        router.RegisterWriter(new FormatHandlerRegistration<IMetadataWriter>(
            "OOXML",
            ooxmlWriter,
            [".docx", ".xlsx", ".pptx"],
            MatchesZipMagic));

        // Audio
        router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
            "Audio",
            audioReader,
            [".mp3", ".wav"],
            MatchesAudioMagic));

        router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
            "Audio",
            audioScrubber,
            [".mp3", ".wav"],
            MatchesAudioMagic));

        router.RegisterWriter(new FormatHandlerRegistration<IMetadataWriter>(
            "Audio",
            audioWriter,
            [".mp3", ".wav"],
            MatchesAudioMagic));

        // Video
        router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
            "Video",
            videoReader,
            [".mp4", ".mov", ".m4v", ".mkv", ".webm"],
            MatchesVideoMagic));

        router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
            "Video",
            videoScrubber,
            [".mp4", ".mov", ".m4v", ".mkv", ".webm"],
            MatchesVideoMagic));

        router.RegisterWriter(new FormatHandlerRegistration<IMetadataWriter>(
            "Video",
            videoWriter,
            [".mp4", ".mov", ".m4v", ".mkv", ".webm"],
            MatchesVideoMagic));

        return router;
    }

    /// <summary>
    /// Gets all unique supported file extensions registered across readers.
    /// </summary>
    public IReadOnlyList<string> GetSupportedReaderExtensions() =>
        _readerRegistrations.SelectMany(r => r.Extensions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool MatchesPdfMagic(byte[] bytes) =>
        PdfMetadataReader.MatchesPdfMagic(bytes);

    public static bool MatchesImageMagic(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return true;
        if (bytes.Length >= 4 && ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                                  (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A))) return true;
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P') return true;
        if (bytes.Length >= 12 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
            return brand is "heic" or "heif" or "heix" or "hevc" or "hevx" or "mif1" or "msf1";
        }
        return false;
    }

    public static bool MatchesZipMagic(byte[] bytes) =>
        bytes.Length >= 4 &&
        bytes[0] == (byte)'P' &&
        bytes[1] == (byte)'K' &&
        bytes[2] is 0x03 or 0x05 or 0x07 &&
        bytes[3] is 0x04 or 0x06 or 0x08;

    public static bool MatchesAudioMagic(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33) return true;
        if (bytes.Length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return true;
        return false;
    }

    public static bool MatchesVideoMagic(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[4] == 'f' && bytes[5] == 't' && bytes[6] == 'y' && bytes[7] == 'p') return true;
        if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3) return true;
        return false;
    }

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
