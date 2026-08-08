using Metawipe.Core.Abstractions;
using Metawipe.Core.Models;
using Metawipe.Core.Routing;

namespace Metawipe.Core.Tests;

public class FormatRouterTests
{
    [Fact]
    public void ResolveReader_MatchesByExtension()
    {
        var jpegReader = new FakeReader("jpeg-reader");
        var router = BuildRouter(jpegReader, new FakeReader("png-reader"));

        var result = router.ResolveReader("photo.JPG");

        Assert.True(result.IsSupported);
        Assert.Equal("JPEG", result.FormatName);
        Assert.Same(jpegReader, result.Handler);
    }

    [Fact]
    public void ResolveReader_MatchesByMagicBytes_WhenExtensionUnknown()
    {
        var jpegReader = new FakeReader("jpeg-reader");
        var router = BuildRouter(jpegReader, new FakeReader("png-reader"));

        var jpegMagicBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var result = router.ResolveReader("blob.bin", jpegMagicBytes);

        Assert.True(result.IsSupported);
        Assert.Equal("JPEG", result.FormatName);
        Assert.Same(jpegReader, result.Handler);
    }

    [Fact]
    public void ResolveReader_ReturnsUnsupported_WhenNoRegistrationMatches()
    {
        var router = BuildRouter(new FakeReader("jpeg-reader"), new FakeReader("png-reader"));

        var result = router.ResolveReader("notes.txt", new byte[] { 0x10, 0x20 });

        Assert.False(result.IsSupported);
        Assert.Null(result.Handler);
        Assert.Contains("Unsupported file format", result.Message);
    }

    [Fact]
    public void ResolveScrubber_MatchesByExtension()
    {
        var pngScrubber = new FakeScrubber("png-scrubber");
        var router = new FormatRouter();
        router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
            "PNG",
            pngScrubber,
            [".png"],
            bytes => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50));

        var result = router.ResolveScrubber("image.png");

        Assert.True(result.IsSupported);
        Assert.Equal("PNG", result.FormatName);
        Assert.Same(pngScrubber, result.Handler);
    }

    private static FormatRouter BuildRouter(IMetadataReader jpegReader, IMetadataReader pngReader)
    {
        var router = new FormatRouter();
        router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
            "JPEG",
            jpegReader,
            [".jpg", ".jpeg"],
            bytes => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF));
        router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
            "PNG",
            pngReader,
            [".png"],
            bytes => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50));

        return router;
    }

    private sealed class FakeReader(string name) : IMetadataReader
    {
        public string Name { get; } = name;

        public bool CanRead(string filePath, byte[]? magicBytes = null) => true;

        public Task<MetadataDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MetadataDocument(filePath));
    }

    private sealed class FakeScrubber(string name) : IMetadataScrubber
    {
        public string Name { get; } = name;

        public bool CanScrub(string filePath, byte[]? magicBytes = null) => true;

        public Task<ScrubResult> ScrubAsync(
            string inputPath,
            string outputPath,
            ScrubProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScrubResult(inputPath, outputPath, 0, 0, true));
    }
}
