using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Writers;
using Xunit;

namespace Metdatwip.Core.Tests;

public sealed class ImageMetadataWriterTests
{
    private readonly RuleBasedSensitivityClassifier _classifier = new();

    [Theory]
    [InlineData("jpeg-with-exif-gps.jpg")]
    [InlineData("png-with-exif-gps.png")]
    public async Task WriteAsync_ModifiesExifMetadataFields(string fixtureName)
    {
        var sourceFixture = GetFixturePath(fixtureName);
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, fixtureName);
            var outputPath = Path.Combine(tempDirectory, "edited_" + fixtureName);
            File.Copy(sourceFixture, inputPath);

            var writer = new ImageMetadataWriter(_classifier);
            var reader = new ImageMetadataReader(_classifier);

            var edits = new List<MetadataEdit>
            {
                new("EXIF", "Artist", "John Doe"),
                new("EXIF", "Copyright", "2026 Test Suite"),
            };

            var result = await writer.WriteAsync(inputPath, outputPath, edits);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(outputPath));
            Assert.True(result.AppliedEdits > 0);

            var afterDoc = await reader.ReadAsync(outputPath);
            var artistField = afterDoc.Fields.FirstOrDefault(f => f.Name.Equals("Artist", StringComparison.OrdinalIgnoreCase));
            var copyrightField = afterDoc.Fields.FirstOrDefault(f => f.Name.Equals("Copyright", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(artistField);
            Assert.Contains("John Doe", artistField.Value);

            Assert.NotNull(copyrightField);
            Assert.Contains("2026 Test Suite", copyrightField.Value);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_DoesNotModifyOriginalInputFile()
    {
        var sourceFixture = GetFixturePath("jpeg-with-exif-gps.jpg");
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "original.jpg");
            var outputPath = Path.Combine(tempDirectory, "edited.jpg");
            File.Copy(sourceFixture, inputPath);

            var beforeBytes = await File.ReadAllBytesAsync(inputPath);

            var writer = new ImageMetadataWriter(_classifier);
            var edits = new List<MetadataEdit> { new("EXIF", "Software", "MetaWipe v1.0") };

            var result = await writer.WriteAsync(inputPath, outputPath, edits);

            Assert.True(result.IsSuccess);
            var afterBytes = await File.ReadAllBytesAsync(inputPath);
            Assert.Equal(beforeBytes, afterBytes);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    private static string GetFixturePath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", filename);

    private static string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "MetdatwipWriterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
