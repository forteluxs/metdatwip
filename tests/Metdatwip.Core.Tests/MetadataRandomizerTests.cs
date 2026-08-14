using Metdatwip.Core.Writers;

namespace Metdatwip.Core.Tests;

public sealed class MetadataRandomizerTests
{
    [Fact]
    public void GenerateImageEdits_ReturnsExpectedExifFields()
    {
        var edits = MetadataRandomizer.GenerateImageEdits();

        Assert.NotEmpty(edits);
        Assert.All(edits, e => Assert.Equal("EXIF", e.Group));
        Assert.Contains(edits, e => e.Name == "Make" && !string.IsNullOrWhiteSpace(e.NewValue));
        Assert.Contains(edits, e => e.Name == "Model" && !string.IsNullOrWhiteSpace(e.NewValue));
        Assert.Contains(edits, e => e.Name == "Artist" && !string.IsNullOrWhiteSpace(e.NewValue));
        Assert.Contains(edits, e => e.Name == "Software" && !string.IsNullOrWhiteSpace(e.NewValue));
        Assert.Contains(edits, e => e.Name == "Copyright" && !string.IsNullOrWhiteSpace(e.NewValue));
    }

    [Fact]
    public void GenerateOoxmlEdits_ReturnsExpectedCoreAndAppFields()
    {
        var edits = MetadataRandomizer.GenerateOoxmlEdits();

        Assert.NotEmpty(edits);
        Assert.Contains(edits, e => e.Group == "OOXML-Core" && e.Name == "creator");
        Assert.Contains(edits, e => e.Group == "OOXML-Core" && e.Name == "lastModifiedBy");
        Assert.Contains(edits, e => e.Group == "OOXML-App" && e.Name == "Company");
        Assert.Contains(edits, e => e.Group == "OOXML-App" && e.Name == "Application");
    }

    [Fact]
    public void GenerateAudioEdits_ReturnsExpectedId3Fields()
    {
        var edits = MetadataRandomizer.GenerateAudioEdits();

        Assert.NotEmpty(edits);
        Assert.All(edits, e => Assert.Equal("ID3v2", e.Group));
        Assert.Contains(edits, e => e.Name == "Title");
        Assert.Contains(edits, e => e.Name == "Artist");
        Assert.Contains(edits, e => e.Name == "Album");
        Assert.Contains(edits, e => e.Name == "Year");
    }

    [Fact]
    public void GenerateVideoEdits_ReturnsExpectedMp4Fields()
    {
        var edits = MetadataRandomizer.GenerateVideoEdits();

        Assert.NotEmpty(edits);
        Assert.All(edits, e => Assert.Equal("MP4-Metadata", e.Group));
        Assert.Contains(edits, e => e.Name == "Title");
        Assert.Contains(edits, e => e.Name == "Artist");
        Assert.Contains(edits, e => e.Name == "Album");
        Assert.Contains(edits, e => e.Name == "Year");
    }

    [Fact]
    public void GeneratePdfEdits_ReturnsExpectedPdfFields()
    {
        var edits = MetadataRandomizer.GeneratePdfEdits();

        Assert.NotEmpty(edits);
        Assert.All(edits, e => Assert.Equal("PDF-Info", e.Group));
        Assert.Contains(edits, e => e.Name == "Title");
        Assert.Contains(edits, e => e.Name == "Author");
        Assert.Contains(edits, e => e.Name == "Creator");
        Assert.Contains(edits, e => e.Name == "Producer");
        Assert.Contains(edits, e => e.Name == "CreationDate");
    }
}
