using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Scrubbers;

namespace Metdatwip.Core.Tests;

public sealed class PdfMetadataScrubberTests
{
    private readonly PdfMetadataScrubber _scrubber;
    private readonly PdfMetadataReader _reader;

    public PdfMetadataScrubberTests()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _scrubber = new PdfMetadataScrubber(classifier);
        _reader = new PdfMetadataReader(classifier);
    }

    [Fact]
    public void CanScrub_ReturnsTrue_ForPdf()
    {
        Assert.True(_scrubber.CanScrub("document.pdf"));
    }

    [Fact]
    public async Task ScrubAsync_RemovesInfoAndXmp_FromPdf()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-pdf-scrub-in-{Guid.NewGuid():N}.pdf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-pdf-scrub-out-{Guid.NewGuid():N}.pdf");

        try
        {
            PdfTestFactory.CreatePdfWithMetadata(
                tempInput,
                title: "Top Secret Strategy",
                author: "Agent Fox",
                subject: "Classified Strategy",
                creator: "Writer Tool",
                producer: "PDF Writer");

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
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-pdf-inplace-{Guid.NewGuid():N}.pdf");

        try
        {
            PdfTestFactory.CreatePdfWithMetadata(tempFile, "Inplace Title", "Inplace Author");

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
