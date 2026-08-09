using Metawipe.Core.Classification;
using Metawipe.Core.Readers;

namespace Metawipe.Core.Tests;

public sealed class ImageMetadataReaderTests
{
    private readonly ImageMetadataReader _reader =
        new(new RuleBasedSensitivityClassifier());

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.png")]
    [InlineData("photo.tif")]
    [InlineData("photo.tiff")]
    [InlineData("photo.heic")]
    [InlineData("photo.heif")]
    [InlineData("photo.webp")]
    public void CanRead_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_reader.CanRead(fileName));
    }

    [Fact]
    public async Task ReadAsync_ExtractsAndFlagsSensitiveFields_FromJpegFixture()
    {
        var fixturePath = GetFixturePath("jpeg-with-exif-gps.jpg");

        var document = await _reader.ReadAsync(fixturePath);

        Assert.NotEmpty(document.Fields);
        Assert.Contains(document.Fields, field => field.Group == "GPS" && field.Name.Contains("Latitude", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(document.Fields, field => field.Name.Contains("Serial", StringComparison.OrdinalIgnoreCase) && field.IsSensitive);
        Assert.Contains(document.Fields, field => field.Name.Contains("Artist", StringComparison.OrdinalIgnoreCase) && field.IsSensitive);
        Assert.Contains(document.Fields, field => field.Name.Contains("Software", StringComparison.OrdinalIgnoreCase) && field.IsSensitive);
    }

    [Fact]
    public async Task ReadAsync_ExtractsExifAndGps_FromPngFixture()
    {
        var fixturePath = GetFixturePath("png-with-exif-gps.png");

        var document = await _reader.ReadAsync(fixturePath);

        Assert.NotEmpty(document.Fields);
        Assert.Contains(document.Fields, field => field.Group == "EXIF");
        Assert.Contains(document.Fields, field => field.Group == "GPS" && field.Name.Contains("Longitude", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetFixturePath(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");
        return path;
    }
}
