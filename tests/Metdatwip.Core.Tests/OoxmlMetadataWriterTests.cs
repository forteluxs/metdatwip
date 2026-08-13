using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Writers;
using Xunit;

namespace Metdatwip.Core.Tests;

public sealed class OoxmlMetadataWriterTests
{
    private readonly RuleBasedSensitivityClassifier _classifier = new();

    [Fact]
    public async Task WriteAsync_ModifiesDocxCoreProperties()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var inputPath = OoxmlTestPackageFactory.CreatePackageWithMetadata(tempDirectory, ".docx");
            var outputPath = Path.Combine(tempDirectory, "edited_sample.docx");

            var writer = new OoxmlMetadataWriter(_classifier);
            var reader = new OoxmlMetadataReader(_classifier);

            var edits = new List<MetadataEdit>
            {
                new("OOXML-Core", "creator", "New Author"),
                new("OOXML-Core", "lastModifiedBy", "New Editor"),
            };

            var result = await writer.WriteAsync(inputPath, outputPath, edits);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(2, result.AppliedEdits);

            var afterDoc = await reader.ReadAsync(outputPath);
            var creatorField = afterDoc.Fields.FirstOrDefault(f => f.Name.Equals("creator", StringComparison.OrdinalIgnoreCase));
            var editorField = afterDoc.Fields.FirstOrDefault(f => f.Name.Equals("lastModifiedBy", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(creatorField);
            Assert.Equal("New Author", creatorField.Value);

            Assert.NotNull(editorField);
            Assert.Equal("New Editor", editorField.Value);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "OoxmlWriterTests_" + Guid.NewGuid().ToString("N"));
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
