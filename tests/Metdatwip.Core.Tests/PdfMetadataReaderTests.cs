using Metdatwip.Core.Classification;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Tests;

public sealed class PdfMetadataReaderTests
{
    private readonly PdfMetadataReader _reader = new(new RuleBasedSensitivityClassifier());

    [Fact]
    public void CanRead_ReturnsTrue_ForPdfExtension()
    {
        Assert.True(_reader.CanRead("document.pdf"));
    }

    [Fact]
    public void CanRead_ReturnsTrue_ForPdfMagicBytes()
    {
        var pdfMagic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }; // "%PDF-1.4"
        Assert.True(_reader.CanRead("unknown.bin", pdfMagic));
    }

    [Fact]
    public async Task ReadAsync_ExtractsInfoAndXmp_FromPdf()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-pdf-{Guid.NewGuid():N}.pdf");

        try
        {
            PdfTestFactory.CreatePdfWithMetadata(
                tempFile,
                title: "Financial Audit Report",
                author: "Sarah Jenkins",
                subject: "Q3 Earnings",
                creator: "Excel Export Engine",
                producer: "Acrobat Distiller 11.0",
                creationDate: "D:20240814120000+00'00'");

            var doc = await _reader.ReadAsync(tempFile);

            Assert.NotEmpty(doc.Fields);

            // PDF-Info assertions
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Title" && f.Value == "Financial Audit Report");
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Author" && f.Value == "Sarah Jenkins" && f.IsSensitive);
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Creator" && f.Value == "Excel Export Engine" && f.IsSensitive);
            Assert.Contains(doc.Fields, f => f.Group == "PDF-Info" && f.Name == "Producer" && f.Value == "Acrobat Distiller 11.0" && f.IsSensitive);

            // XMP assertions
            Assert.Contains(doc.Fields, f => f.Name == "creator" || f.Name == "CreatorTool");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
