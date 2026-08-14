using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Writers;

namespace Metdatwip.Core.Tests;

public sealed class VideoMetadataWriterTests
{
    private readonly VideoMetadataWriter _writer;
    private readonly VideoMetadataReader _reader;

    public VideoMetadataWriterTests()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _writer = new VideoMetadataWriter(classifier);
        _reader = new VideoMetadataReader(classifier);
    }

    [Theory]
    [InlineData("video.mp4")]
    [InlineData("video.mov")]
    [InlineData("video.m4v")]
    public void CanWrite_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_writer.CanWrite(fileName));
    }

    [Fact]
    public async Task WriteAsync_AppliesEdits_ToMp4()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-video-edit-in-{Guid.NewGuid():N}.mp4");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-video-edit-out-{Guid.NewGuid():N}.mp4");

        try
        {
            VideoTestFactory.CreateMp4WithMetadata(tempInput, "Old Video Title", "Old Video Artist", "Old Album", "2010");

            var edits = new List<MetadataEdit>
            {
                new("MP4-Metadata", "Title", "Avatar: The Way of Water"),
                new("MP4-Metadata", "Artist", "James Cameron"),
                new("MP4-Metadata", "Album", "Avatar Collection"),
                new("MP4-Metadata", "Year", "2022"),
                new("MP4-Metadata", "Software", "DaVinci Resolve"),
            };

            var writeResult = await _writer.WriteAsync(tempInput, tempOutput, edits);

            Assert.True(writeResult.IsSuccess);
            Assert.Equal(5, writeResult.AppliedEdits);

            var doc = await _reader.ReadAsync(tempOutput);
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Title" && f.Value.Contains("Avatar: The Way of Water"));
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Artist" && f.Value.Contains("James Cameron"));
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Album" && f.Value.Contains("Avatar Collection"));
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Year" && f.Value.Contains("2022"));
            Assert.Contains(doc.Fields, f => f.Group == "MP4-Metadata" && f.Name == "Software" && f.Value.Contains("DaVinci Resolve"));
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
