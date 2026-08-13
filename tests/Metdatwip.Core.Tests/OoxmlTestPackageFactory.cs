using System.IO.Packaging;
using System.Text;

namespace Metdatwip.Core.Tests;

internal static class OoxmlTestPackageFactory
{
    public static string CreatePackageWithMetadata(string rootDirectory, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        Directory.CreateDirectory(rootDirectory);

        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var packagePath = Path.Combine(rootDirectory, $"sample{normalizedExtension}");

        using var package = Package.Open(packagePath, FileMode.Create, FileAccess.ReadWrite);

        var bodyPartPath = normalizedExtension.ToLowerInvariant() switch
        {
            ".docx" => "word/document.xml",
            ".xlsx" => "xl/workbook.xml",
            ".pptx" => "ppt/presentation.xml",
            _ => throw new NotSupportedException($"Unsupported test extension: {normalizedExtension}"),
        };

        WritePart(
            package,
            bodyPartPath,
            "application/xml",
            "<root><body>Hello metdatwip</body><version>1</version></root>");

        WritePart(
            package,
            "docProps/core.xml",
            "application/vnd.openxmlformats-package.core-properties+xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                               xmlns:dc="http://purl.org/dc/elements/1.1/"
                               xmlns:dcterms="http://purl.org/dc/terms/"
                               xmlns:dcmitype="http://purl.org/dc/dcmitype/"
                               xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dc:title>Incident Report</dc:title>
              <dc:creator>Alex Example</dc:creator>
              <cp:lastModifiedBy>BuildAgent42</cp:lastModifiedBy>
            </cp:coreProperties>
            """);

        WritePart(
            package,
            "docProps/app.xml",
            "application/vnd.openxmlformats-officedocument.extended-properties+xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <Application>Metdatwip Test Suite</Application>
              <Company>Nous Research</Company>
            </Properties>
            """);

        WritePart(
            package,
            "docProps/custom.xml",
            "application/vnd.openxmlformats-officedocument.custom-properties+xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="ClientName">
                <vt:lpwstr>Jordan Smith</vt:lpwstr>
              </property>
              <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="3" name="MatterNumber">
                <vt:i4>1337</vt:i4>
              </property>
            </Properties>
            """);

        return packagePath;
    }

    public static string ReadPartText(string packagePath, string partPath)
    {
        using var package = Package.Open(packagePath, FileMode.Open, FileAccess.Read);
        var part = package.GetPart(PackUriHelper.CreatePartUri(new Uri(partPath, UriKind.Relative)));

        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WritePart(Package package, string partPath, string contentType, string xml)
    {
        var partUri = PackUriHelper.CreatePartUri(new Uri(partPath, UriKind.Relative));
        var part = package.CreatePart(partUri, contentType, CompressionOption.Maximum);

        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(xml);
    }
}
