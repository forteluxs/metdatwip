using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Writers;

namespace Metdatwip.Core.Tests;

public sealed class PdfMetadataWriterTests
{
    private readonly PdfMetadataWriter _writer;
    private readonly PdfMetadataReader _reader;

    public PdfMetadataWriterTests()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _writer = new PdfMetadataWriter(classifier);
        _reader = new PdfMetadataReader(classifier);
    }

    [Fact]
    public void CanWrite_ReturnsTrue_ForPdf()
    {
        Assert.True(_writer.CanWrite("document.pdf"));
    }

    [Fact]
    public async Task WriteAsync_AppliesEdits_ToPdf()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"test-pdf-edit-in-{Guid.NewGuid():N}.pdf");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"test-pdf-edit-out-{Guid.NewGuid():N}.pdf");

        try
        {
            PdfTestFactory.CreatePdfWithMetadata(
                tempInput,
                title: "Old PDF Title",
                author: "Old Author",
                subject: "Old Subject",
                creator: "Old Creator",
                producer: "Old Producer");

            var edits = new List<MetadataEdit>
            {
                new("PDF-Info", "Title", "Brand New PDF Title"),
                new("PDF-Info", "Author", "Elena Rostova"),
                new("PDF-Info", "Subject", "Privacy Shield Protocol"),
                new("PDF-Info", "Producer", "Metdatwip PDF Writer 1.1"),
            };

            var writeResult = await _writer.WriteAsync(tempInput, tempOutput, edits);

            Assert.True(writeResult.IsSuccess);
            Assert.Equal(4, writeResult.AppliedEdits);

            var doc = await _reader.ReadAsync(tempOutput);
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Title" && f.Value == "Brand New PDF Title");
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Author" && f.Value == "Elena Rostova");
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Subject" && f.Value == "Privacy Shield Protocol");
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Producer" && f.Value == "Metdatwip PDF Writer 1.1");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
