using Metawipe.App.Services;

namespace Metawipe.App.Tests;

public class MetadataInspectionServiceTests
{
    [Fact]
    public async Task InspectAsync_WithImageFile_ReturnsSensitiveMetadata()
    {
        var service = new MetadataInspectionService();
        var fixturePath = GetFixturePath("jpeg-with-exif-gps.jpg");

        var result = await service.InspectAsync([fixturePath]);

        Assert.False(result.WasFolderInput);
        Assert.Equal(Path.GetFullPath(fixturePath), result.SourcePath);
        Assert.NotEmpty(result.Document.Fields);
        Assert.Contains(result.Document.Fields, field => field.IsSensitive);
    }

    [Fact]
    public async Task InspectAsync_WithFolder_InspectsFirstSupportedFile()
    {
        var service = new MetadataInspectionService();
        var fixturePath = GetFixturePath("png-with-exif-gps.png");

        var tempDir = Path.Combine(Path.GetTempPath(), $"metawipe-app-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var copiedFixture = Path.Combine(tempDir, Path.GetFileName(fixturePath));
            File.Copy(fixturePath, copiedFixture);

            var result = await service.InspectAsync([tempDir]);

            Assert.True(result.WasFolderInput);
            Assert.Equal(Path.GetFullPath(copiedFixture), result.SourcePath);
            Assert.NotEmpty(result.Document.Fields);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static string GetFixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
