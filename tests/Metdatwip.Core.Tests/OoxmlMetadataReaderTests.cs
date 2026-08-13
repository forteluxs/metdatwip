using Metdatwip.Core.Classification;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Tests;

public sealed class OoxmlMetadataReaderTests
{
    private readonly OoxmlMetadataReader _reader =
        new(new RuleBasedSensitivityClassifier());

    [Theory]
    [InlineData("report.docx")]
    [InlineData("sheet.xlsx")]
    [InlineData("slides.pptx")]
    public void CanRead_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_reader.CanRead(fileName));
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".pptx")]
    public async Task ReadAsync_ExtractsCoreAppAndCustomMetadata(string extension)
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var packagePath = OoxmlTestPackageFactory.CreatePackageWithMetadata(tempDirectory, extension);

            var document = await _reader.ReadAsync(packagePath);

            Assert.NotEmpty(document.Fields);
            Assert.Contains(document.Fields, field => field.Group == "OOXML-Core" && field.Name == "title" && field.Value == "Incident Report");
            Assert.Contains(document.Fields, field => field.Group == "OOXML-Core" && field.Name == "creator" && field.IsSensitive);
            Assert.Contains(document.Fields, field => field.Group == "OOXML-App" && field.Name == "Company" && field.Value == "Nous Research");
            Assert.Contains(document.Fields, field => field.Group == "OOXML-Custom" && field.Name == "ClientName" && field.IsSensitive);
            Assert.Contains(document.Fields, field => field.Group == "OOXML-Custom" && field.Name == "MatterNumber" && field.Value == "1337");
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metdatwip-tests-{Guid.NewGuid():N}");
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
