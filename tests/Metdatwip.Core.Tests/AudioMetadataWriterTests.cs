using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Writers;

namespace Metdatwip.Core.Tests;

public sealed class AudioMetadataWriterTests
{
    private readonly AudioMetadataWriter _writer;
    private readonly AudioMetadataReader _reader;

    public AudioMetadataWriterTests()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _writer = new AudioMetadataWriter(classifier);
        _reader = new AudioMetadataReader(classifier);
    }

    [Theory]
    [InlineData("track.mp3")]
    [InlineData("track.wav")]
    public void CanWrite_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(_writer.CanWrite(fileName));
    }

    [Fact]
    public async Task WriteAsync_AppliesEdits_ToMp3()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-edit-input-{Guid.NewGuid():N}.mp3");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-edit-output-{Guid.NewGuid():N}.mp3");

        try
        {
            AudioTestFactory.CreateMp3WithMetadata(tempInput, "Original Title", "Original Artist", "Original Album", "2020");

            var edits = new List<MetadataEdit>
            {
                new("ID3v2", "Title", "New Edited Title"),
                new("ID3v2", "Artist", "New Edited Artist"),
                new("ID3v2", "Album", "New Edited Album"),
                new("ID3v2", "Year", "2025"),
            };

            var writeResult = await _writer.WriteAsync(tempInput, tempOutput, edits);

            Assert.True(writeResult.IsSuccess);
            Assert.Equal(4, writeResult.AppliedEdits);

            var doc = await _reader.ReadAsync(tempOutput);
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Title" && f.Value == "New Edited Title");
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Artist" && f.Value == "New Edited Artist");
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Album" && f.Value == "New Edited Album");
            Assert.Contains(doc.Fields, f => f.Group == "ID3v2" && f.Name == "Year" && f.Value == "2025");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task WriteAsync_AppliesEdits_ToWav()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-edit-input-{Guid.NewGuid():N}.wav");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-edit-output-{Guid.NewGuid():N}.wav");

        try
        {
            AudioTestFactory.CreateWavWithMetadata(tempInput, "Original Title", "Original Artist", "Original Album", "2020");

            var edits = new List<MetadataEdit>
            {
                new("RIFF-INFO", "Title", "WAV Modern Title"),
                new("RIFF-INFO", "Artist", "WAV Modern Artist"),
                new("RIFF-INFO", "Album", "WAV Modern Album"),
                new("RIFF-INFO", "Year", "2026"),
            };

            var writeResult = await _writer.WriteAsync(tempInput, tempOutput, edits);

            Assert.True(writeResult.IsSuccess);

            var doc = await _reader.ReadAsync(tempOutput);
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Title" && f.Value == "WAV Modern Title");
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Artist" && f.Value == "WAV Modern Artist");
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Album" && f.Value == "WAV Modern Album");
            Assert.Contains(doc.Fields, f => f.Group == "RIFF-INFO" && f.Name == "Year" && f.Value == "2026");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
