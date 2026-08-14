using Metdatwip.Core.Classification;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Tests;

public sealed class AudioMetadataReaderTests
{
    private readonly AudioMetadataReader _reader = new(new RuleBasedSensitivityClassifier());

    [Theory]
    [InlineData("audio.mp3")]
    [InlineData("audio.wav")]
    public void CanRead_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_reader.CanRead(fileName));
    }

    [Fact]
    public void CanRead_ReturnsTrue_ForId3AndRiffMagicBytes()
    {
        var id3Magic = new byte[] { 0x49, 0x44, 0x33, 0x03 }; // "ID3\x03"
        var riffMagic = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"

        Assert.True(_reader.CanRead("unknown.bin", id3Magic));
        Assert.True(_reader.CanRead("unknown.bin", riffMagic));
    }

    [Fact]
    public async Task ReadAsync_ExtractsId3Tags_FromMp3()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-audio-{Guid.NewGuid():N}.mp3");

        try
        {
            AudioTestFactory.CreateMp3WithMetadata(
                tempFile,
                title: "Bohemian Rhapsody",
                artist: "Queen",
                album: "A Night at the Opera",
                year: "1975");

            var doc = await _reader.ReadAsync(tempFile);

            Assert.NotEmpty(doc.Fields);
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Title" && f.Value == "Bohemian Rhapsody");
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Artist" && f.Value == "Queen" && f.IsSensitive);
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Album" && f.Value == "A Night at the Opera");
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Year" && f.Value == "1975");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadAsync_ExtractsListInfoTags_FromWav()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-audio-{Guid.NewGuid():N}.wav");

        try
        {
            AudioTestFactory.CreateWavWithMetadata(
                tempFile,
                title: "Imagine",
                artist: "John Lennon",
                album: "Imagine",
                year: "1971");

            var doc = await _reader.ReadAsync(tempFile);

            Assert.NotEmpty(doc.Fields);
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Title" && f.Value == "Imagine");
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Artist" && f.Value == "John Lennon" && f.IsSensitive);
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Album" && f.Value == "Imagine");
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Year" && f.Value == "1971");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
