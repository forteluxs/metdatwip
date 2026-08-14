using System.Text;

namespace Metdatwip.Core.Tests;

internal static class PdfTestFactory
{
    public static string CreatePdfWithMetadata(
        string targetPath,
        string title = "Confidential Blueprint",
        string author = "Dr. Jane Doe",
        string subject = "Classified Project Alpha",
        string creator = "Acme CAD Software 2024",
        string producer = "Acme PDF Generator 1.0",
        string creationDate = "D:20240814140000+00'00'")
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        var xmpPayload = $"""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about=""
                    xmlns:dc="http://purl.org/dc/elements/1.1/"
                    xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                    xmlns:pdf="http://ns.adobe.com/pdf/1.3/">
                  <dc:title>{title}</dc:title>
                  <dc:creator>{author}</dc:creator>
                  <xmp:CreatorTool>{creator}</xmp:CreatorTool>
                  <pdf:Producer>{producer}</pdf:Producer>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """;

        var xmpBytes = Encoding.UTF8.GetBytes(xmpPayload);

        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("%\u00E2\u00E3\u00CF\u00D3");

        // Object 1: Catalog
        sb.AppendLine("1 0 obj");
        sb.AppendLine("<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>");
        sb.AppendLine("endobj");

        // Object 2: Pages
        sb.AppendLine("2 0 obj");
        sb.AppendLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        sb.AppendLine("endobj");

        // Object 3: Page
        sb.AppendLine("3 0 obj");
        sb.AppendLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>");
        sb.AppendLine("endobj");

        // Object 4: Content stream
        var content = "BT /F1 24 Tf 100 700 Td (Hello Metdatwip) Tj ET";
        sb.AppendLine("4 0 obj");
        sb.AppendLine($"<< /Length {content.Length} >>");
        sb.AppendLine("stream");
        sb.AppendLine(content);
        sb.AppendLine("endstream");
        sb.AppendLine("endobj");

        // Object 5: Metadata (XMP) stream
        sb.AppendLine("5 0 obj");
        sb.AppendLine($"<< /Type /Metadata /Subtype /XML /Length {xmpBytes.Length} >>");
        sb.AppendLine("stream");
        sb.Append(xmpPayload);
        sb.AppendLine();
        sb.AppendLine("endstream");
        sb.AppendLine("endobj");

        // Object 6: Info dictionary
        sb.AppendLine("6 0 obj");
        sb.AppendLine("<<");
        sb.AppendLine($"  /Title ({EscapePdf(title)})");
        sb.AppendLine($"  /Author ({EscapePdf(author)})");
        sb.AppendLine($"  /Subject ({EscapePdf(subject)})");
        sb.AppendLine($"  /Creator ({EscapePdf(creator)})");
        sb.AppendLine($"  /Producer ({EscapePdf(producer)})");
        sb.AppendLine($"  /CreationDate ({creationDate})");
        sb.AppendLine(">>");
        sb.AppendLine("endobj");

        // Trailer
        sb.AppendLine("xref");
        sb.AppendLine("0 7");
        sb.AppendLine("0000000000 65535 f ");
        sb.AppendLine("0000000015 00000 n ");
        sb.AppendLine("0000000074 00000 n ");
        sb.AppendLine("0000000120 00000 n ");
        sb.AppendLine("0000000213 00000 n ");
        sb.AppendLine("0000000315 00000 n ");
        sb.AppendLine("0000000450 00000 n ");
        sb.AppendLine("trailer");
        sb.AppendLine("<<");
        sb.AppendLine("  /Size 7");
        sb.AppendLine("  /Root 1 0 R");
        sb.AppendLine("  /Info 6 0 R");
        sb.AppendLine(">>");
        sb.AppendLine("startxref");
        sb.AppendLine("600");
        sb.AppendLine("%%EOF");

        File.WriteAllText(targetPath, sb.ToString(), Encoding.Latin1);
        return targetPath;
    }

    private static string EscapePdf(string val) =>
        val.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
