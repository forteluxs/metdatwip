using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Scrubbers;

namespace Metdatwip.Core.Tests;

public sealed class AudioMetadataScrubberTests
{
    private readonly AudioMetadataScrubber _scrubber;
    private readonly AudioMetadataReader _reader;

    public AudioMetadataScrubberTests()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _scrubber = new AudioMetadataScrubber(classifier);
        _reader = new AudioMetadataReader(classifier);
    }

    [Theory]
    [InlineData("sample.mp3")]
    [InlineData("sample.wav")]
    public void CanScrub_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_scrubber.CanScrub(fileName));
    }

    [Fact]
    public async Task ScrubAsync_RemovesMetadata_FromMp3()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-scrub-input-{Guid.NewGuid():N}.mp3");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-scrub-output-{Guid.NewGuid():N}.mp3");

        try
        {
            AudioTestFactory.CreateMp3WithMetadata(
                tempInput,
                title: "Sensitive Song",
                artist: "Secret Artist",
                album: "Private Album",
                year: "2024");

            var beforeDoc = await _reader.ReadAsync(tempInput);
            Assert.NotEmpty(beforeDoc.Fields);

            var result = await _scrubber.ScrubAsync(tempInput, tempOutput, ScrubProfile.CreateStripAll());

            Assert.True(result.IsSuccess);
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
    public async Task ScrubAsync_RemovesMetadata_FromWav()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-scrub-input-{Guid.NewGuid():N}.wav");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-scrub-output-{Guid.NewGuid():N}.wav");

        try
        {
            AudioTestFactory.CreateWavWithMetadata(
                tempInput,
                title: "Sensitive Recording",
                artist: "Confidential Speaker",
                album: "Meeting Archive",
                year: "2024");

            var beforeDoc = await _reader.ReadAsync(tempInput);
            Assert.NotEmpty(beforeDoc.Fields);

            var result = await _scrubber.ScrubAsync(tempInput, tempOutput, ScrubProfile.CreateStripAll());

            Assert.True(result.IsSuccess);
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
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-inplace-{Guid.NewGuid():N}.mp3");

        try
        {
            AudioTestFactory.CreateMp3WithMetadata(
                tempFile,
                title: "Overwritten Song",
                artist: "Overwritten Artist",
                album: "Overwritten Album",
                year: "2024");

            var result = await _scrubber.ScrubAsync(tempFile, tempFile, ScrubProfile.CreateStripAll());

            Assert.True(result.IsSuccess);
            var afterDoc = await _reader.ReadAsync(tempFile);
            Assert.Empty(afterDoc.Fields);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
