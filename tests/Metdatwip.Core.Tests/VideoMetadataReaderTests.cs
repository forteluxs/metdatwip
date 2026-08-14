using Metdatwip.Core.Classification;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Tests;

public sealed class VideoMetadataReaderTests
{
    private readonly VideoMetadataReader _reader = new(new RuleBasedSensitivityClassifier());

    [Theory]
    [InlineData("video.mp4")]
    [InlineData("video.mov")]
    [InlineData("video.m4v")]
    [InlineData("video.mkv")]
    [InlineData("video.webm")]
    public void CanRead_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_reader.CanRead(fileName));
    }

    [Fact]
    public void CanRead_ReturnsTrue_ForFtypAndEbmlMagic()
    {
        var ftypMagic = new byte[] { 0, 0, 0, 20, (byte)'f', (byte)'t', (byte)'y', (byte)'p' };
        var ebmlMagic = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00 };

        Assert.True(_reader.CanRead("unknown.bin", ftypMagic));
        Assert.True(_reader.CanRead("unknown.bin", ebmlMagic));
    }

    [Fact]
    public async Task ReadAsync_ExtractsMetadata_FromMp4()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-video-{Guid.NewGuid():N}.mp4");

        try
        {
            VideoTestFactory.CreateMp4WithMetadata(
                tempFile,
                title: "Interstellar Journey",
                artist: "Christopher Nolan",
                album: "SciFi Classics",
                year: "2014");

            var doc = await _reader.ReadAsync(tempFile);

            Assert.NotEmpty(doc.Fields);
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Title" && f.Value.Contains("Interstellar Journey"));
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Artist" && f.Value.Contains("Christopher Nolan"));
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Album" && f.Value.Contains("SciFi Classics"));
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Year" && f.Value.Contains("2014"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadAsync_ExtractsTags_FromMkv()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-video-{Guid.NewGuid():N}.mkv");

        try
        {
            VideoTestFactory.CreateMkvWithMetadata(
                tempFile,
                title: "Oppenheimer",
                artist: "Christopher Nolan");

            var doc = await _reader.ReadAsync(tempFile);

            Assert.NotEmpty(doc.Fields);
            Assert.Contains(doc.Fields, f => f.Group == "MKV-Tags" && f.Name == "TITLE" && f.Value == "Oppenheimer");
            Assert.Contains(doc.Fields, f => f.Group == "MKV-Tags" && f.Name == "ARTIST" && f.Value == "Christopher Nolan");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
