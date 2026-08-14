using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;

namespace Metdatwip.Core.Tests;

public sealed class RuleBasedSensitivityClassifierTests
{
    private readonly RuleBasedSensitivityClassifier _classifier = new();

    [Theory]
    [InlineData("GPS", "AnyTag", "123.45")]
    [InlineData("Location", "Altitude", "100m")]
    [InlineData("Geotag", "Coordinate", "0,0")]
    public void IsSensitive_ReturnsTrue_ForAlwaysSensitiveGroups(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("EXIF", "Artist", "John")]
    [InlineData("EXIF", "Camera Serial Number", "123456")]
    [InlineData("EXIF", "Body Serial Number", "987654")]
    [InlineData("EXIF", "Software", "Photoshop")]
    [InlineData("OOXML-Core", "creator", "Jane Doe")]
    [InlineData("OOXML-Core", "lastModifiedBy", "Agent42")]
    [InlineData("OOXML-App", "Company", "Acme Corp")]
    [InlineData("ID3v2", "Artist", "Queen")]
    [InlineData("MP4-Metadata", "Artist", "Director")]
    public void IsSensitive_ReturnsTrue_ForSensitiveTagNames(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("XMP", "Description", "Contact me at alice.smith@example.com for info")]
    [InlineData("RIFF-INFO", "Comment", "Send feedback to john@sub.domain.org")]
    public void IsSensitive_ReturnsTrue_WhenValueContainsEmail(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("Custom", "Note", "Call +1-555-867-5309 immediately")]
    [InlineData("Custom", "Note", "Emergency: (021) 555-1234")]
    public void IsSensitive_ReturnsTrue_WhenValueContainsPhone(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("Custom", "Note", "Uploaded from server 192.168.1.150")]
    [InlineData("Custom", "Note", "Origin IP: 10.0.4.22")]
    public void IsSensitive_ReturnsTrue_WhenValueContainsIpAddress(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("Custom", "Remarks", "Location taken: 37.7749, -122.4194")]
    [InlineData("Custom", "Remarks", "Coords: -6.2088, 106.8456")]
    public void IsSensitive_ReturnsTrue_WhenValueContainsGpsCoordinates(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("Custom", "SessionId", "123e4567-e89b-12d3-a456-426614174000")]
    [InlineData("Custom", "DocumentGuid", "c73bcdcc-6669-4803-88fa-15d8391bfa95")]
    public void IsSensitive_ReturnsTrue_WhenValueContainsUuid(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("Custom", "HardwareInfo", "Adapter 00:1A:2B:3C:4D:5E connected")]
    [InlineData("Custom", "NetId", "MAC=aa-bb-cc-dd-ee-ff")]
    public void IsSensitive_ReturnsTrue_WhenValueContainsMacAddress(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("Custom", "SourceFilePath", @"C:\Users\JohnDoe\Pictures\export.jpg")]
    [InlineData("Custom", "SourceFilePath", "/home/developer/workspace/project/sample.png")]
    public void IsSensitive_ReturnsTrue_WhenValueContainsUserPaths(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.True(_classifier.IsSensitive(field));
    }

    [Theory]
    [InlineData("Technical", "ColorSpace", "sRGB")]
    [InlineData("Technical", "Compression", "JPEG (old-style)")]
    [InlineData("Technical", "BitsPerSample", "8")]
    [InlineData("Technical", "ImageWidth", "1920")]
    [InlineData("Technical", "ImageHeight", "1080")]
    public void IsSensitive_ReturnsFalse_ForBenignTechnicalMetadata(string group, string name, string value)
    {
        var field = new MetadataField(group, name, value, false, true);
        Assert.False(_classifier.IsSensitive(field));
    }
}
