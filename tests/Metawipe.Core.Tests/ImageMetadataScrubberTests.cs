using Metawipe.Core.Classification;
using Metawipe.Core.Models;
using Metawipe.Core.Readers;
using Metawipe.Core.Scrubbers;

namespace Metawipe.Core.Tests;

public sealed class ImageMetadataScrubberTests
{
    private readonly RuleBasedSensitivityClassifier _classifier = new();

    [Theory]
    [InlineData("jpeg-with-exif-gps.jpg")]
    [InlineData("png-with-exif-gps.png")]
    public async Task ScrubAsync_StripAll_RemovesAllMetadata_AndKeepsOriginalUntouched(string fixtureName)
    {
        var sourceFixture = GetFixturePath(fixtureName);
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, fixtureName);
            var outputPath = Path.Combine(tempDirectory, BuildCleanedFileName(fixtureName));
            File.Copy(sourceFixture, inputPath);

            var scrubber = new ImageMetadataScrubber(_classifier);
            var reader = new ImageMetadataReader(_classifier);

            var beforeInputBytes = await File.ReadAllBytesAsync(inputPath);
            var result = await scrubber.ScrubAsync(inputPath, outputPath, ScrubProfile.CreateStripAll());

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(outputPath));

            var afterInputBytes = await File.ReadAllBytesAsync(inputPath);
            Assert.Equal(beforeInputBytes, afterInputBytes);

            var before = await reader.ReadAsync(inputPath);
            var after = await reader.ReadAsync(outputPath);

            Assert.NotEmpty(before.Fields);
            Assert.Empty(after.Fields);
            Assert.Equal(0, after.Fields.Count(field => field.IsSensitive));
            Assert.Equal(before.Fields.Count, result.RemovedFields + result.KeptFields);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task ScrubAsync_KeepWhitelist_ForExifField_RetainsExifAndGpsMetadata()
    {
        var sourceFixture = GetFixturePath("jpeg-with-exif-gps.jpg");
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "input.jpg");
            var outputPath = Path.Combine(tempDirectory, "input.cleaned.jpg");
            File.Copy(sourceFixture, inputPath);

            var scrubber = new ImageMetadataScrubber(_classifier);
            var reader = new ImageMetadataReader(_classifier);

            var profile = ScrubProfile.CreateKeepWhitelist(["exif/software"]);
            await scrubber.ScrubAsync(inputPath, outputPath, profile);

            var after = await reader.ReadAsync(outputPath);

            Assert.NotEmpty(after.Fields);
            Assert.Contains(after.Fields, field =>
                field.Group == "EXIF" &&
                field.Name.Contains("Software", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(after.Fields, field => field.Group == "GPS");
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task ScrubAsync_Throws_WhenOutputPathMatchesInputPath()
    {
        var sourceFixture = GetFixturePath("png-with-exif-gps.png");
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "input.png");
            File.Copy(sourceFixture, inputPath);

            var scrubber = new ImageMetadataScrubber(_classifier);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scrubber.ScrubAsync(inputPath, inputPath, ScrubProfile.CreateStripAll()));
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    private static string GetFixturePath(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");
        return path;
    }

    private static string BuildCleanedFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        return $"{stem}.cleaned{ext}";
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metawipe-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup in tests.
        }
    }
}
