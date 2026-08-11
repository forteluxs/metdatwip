using Metawipe.Core.Classification;
using Metawipe.Core.Models;
using Metawipe.Core.Readers;
using Metawipe.Core.Scrubbers;

namespace Metawipe.Core.Tests;

public sealed class OoxmlMetadataScrubberTests
{
    private readonly RuleBasedSensitivityClassifier _classifier = new();

    [Theory]
    [InlineData(".docx", "word/document.xml")]
    [InlineData(".xlsx", "xl/workbook.xml")]
    [InlineData(".pptx", "ppt/presentation.xml")]
    public async Task ScrubAsync_StripAll_RemovesAllMetadata_AndPreservesBodyPart(
        string extension,
        string bodyPartPath)
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = OoxmlTestPackageFactory.CreatePackageWithMetadata(tempDirectory, extension);
            var outputPath = Path.Combine(tempDirectory, $"cleaned{extension}");

            var scrubber = new OoxmlMetadataScrubber(_classifier);
            var reader = new OoxmlMetadataReader(_classifier);

            var beforeInputBytes = await File.ReadAllBytesAsync(inputPath);
            var beforeBodyPart = OoxmlTestPackageFactory.ReadPartText(inputPath, bodyPartPath);

            var result = await scrubber.ScrubAsync(inputPath, outputPath, ScrubProfile.CreateStripAll());

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(outputPath));

            var afterInputBytes = await File.ReadAllBytesAsync(inputPath);
            Assert.Equal(beforeInputBytes, afterInputBytes);

            var afterBodyPart = OoxmlTestPackageFactory.ReadPartText(outputPath, bodyPartPath);
            Assert.Equal(beforeBodyPart, afterBodyPart);

            var afterDocument = await reader.ReadAsync(outputPath);
            Assert.Empty(afterDocument.Fields);
            Assert.Equal(0, afterDocument.Fields.Count(field => field.IsSensitive));
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task ScrubAsync_KeepWhitelist_RetainsOnlyWhitelistedOoxmlField()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = OoxmlTestPackageFactory.CreatePackageWithMetadata(tempDirectory, ".docx");
            var outputPath = Path.Combine(tempDirectory, "cleaned.docx");

            var scrubber = new OoxmlMetadataScrubber(_classifier);
            var reader = new OoxmlMetadataReader(_classifier);

            var profile = ScrubProfile.CreateKeepWhitelist(["ooxml-core/title"]);
            await scrubber.ScrubAsync(inputPath, outputPath, profile);

            var afterDocument = await reader.ReadAsync(outputPath);
            Assert.Single(afterDocument.Fields);
            Assert.Contains(afterDocument.Fields, field =>
                field.Group == "OOXML-Core" &&
                field.Name == "title" &&
                field.Value == "Incident Report");
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
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
