using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Scrubbers;

namespace Metdatwip.Core.Tests;

public sealed class VideoMetadataScrubberTests
{
    private readonly VideoMetadataScrubber _scrubber;
    private readonly VideoMetadataReader _reader;

    public VideoMetadataScrubberTests()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _scrubber = new VideoMetadataScrubber(classifier);
        _reader = new VideoMetadataReader(classifier);
    }

    [Theory]
    [InlineData("video.mp4")]
    [InlineData("video.mov")]
    [InlineData("video.m4v")]
    public void CanScrub_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_scrubber.CanScrub(fileName));
    }

    [Fact]
    public async Task ScrubAsync_RemovesMetadata_FromMp4()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-video-scrub-in-{Guid.NewGuid():N}.mp4");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-video-scrub-out-{Guid.NewGuid():N}.mp4");

        try
        {
            VideoTestFactory.CreateMp4WithMetadata(
                tempInput,
                title: "Secret Drone Footage",
                artist: "John Pilot",
                album: "Classified Mission",
                year: "2024");

            var beforeDoc = await _reader.ReadAsync(tempInput);
            Assert.NotEmpty(beforeDoc.Fields);

            var scrubResult = await _scrubber.ScrubAsync(tempInput, tempOutput, ScrubProfile.CreateStripAll());

            Assert.True(scrubResult.IsSuccess);
            Assert.True(File.Exists(tempOutput));

            var afterDoc = await _reader.ReadAsync(tempOutput);
            Assert.Empty(afterDoc.Fields);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task ScrubAsync_InPlaceOverwrite_WorksWithoutError()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-video-inplace-{Guid.NewGuid():N}.mp4");

        try
        {
            VideoTestFactory.CreateMp4WithMetadata(
                tempFile,
                title: "Inplace Video",
                artist: "Inplace Artist",
                album: "Inplace Album",
                year: "2024");

            var scrubResult = await _scrubber.ScrubAsync(tempFile, tempFile, ScrubProfile.CreateStripAll());

            Assert.True(scrubResult.IsSuccess);

            var afterDoc = await _reader.ReadAsync(tempFile);
            Assert.Empty(afterDoc.Fields);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
