using System.Text;
using System.Text.RegularExpressions;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Scrubbers;

/// <summary>
/// Lossless metadata scrubber for PDF documents.
/// Strips the Document Information Dictionary (/Info), Catalog Metadata Streams (/Metadata),
/// and embedded XMP packets, preserving layout and vector/text content.
/// </summary>
public sealed class PdfMetadataScrubber : IMetadataScrubber
{
    private static readonly HashSet<string> SupportedExtensions = [".pdf"];
    private readonly PdfMetadataReader _reader;

    public PdfMetadataScrubber(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new PdfMetadataReader(sensitivityClassifier);
    }

    /// <inheritdoc />
    public string Name => "pdf-metadata-scrubber";

    /// <inheritdoc />
    public bool CanScrub(string filePath, byte[]? magicBytes = null) =>
        _reader.CanRead(filePath, magicBytes);

    /// <inheritdoc />
    public async Task<ScrubResult> ScrubAsync(
        string inputPath,
        string outputPath,
        ScrubProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(profile);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("PDF file not found.", inputPath);
        }

        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        var isSameFile = string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        var targetFile = isSameFile ? Path.Combine(Path.GetTempPath(), "metdatwip_scrub_pdf_" + Guid.NewGuid().ToString("N") + ".pdf") : outputFullPath;

        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
        var beforeDocument = await _reader.ReadAsync(inputPath, cancellationToken);

        var scrubbedBytes = ScrubPdfData(inputBytes);

        var outputDir = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await File.WriteAllBytesAsync(targetFile, scrubbedBytes, cancellationToken);

        if (isSameFile)
        {
            File.Move(targetFile, outputFullPath, overwrite: true);
        }

        var afterDocument = await _reader.ReadAsync(outputFullPath, cancellationToken);
        var removedCount = beforeDocument.Fields.Count - afterDocument.Fields.Count;
        var keptCount = afterDocument.Fields.Count;
        var sensitiveRemaining = afterDocument.Fields.Count(f => f.IsSensitive);

        var message = sensitiveRemaining == 0
            ? "Verification scan: 0 sensitive fields remaining."
            : $"Verification scan: {sensitiveRemaining} sensitive field(s) remaining.";

        return new ScrubResult(
            inputPath,
            outputFullPath,
            Math.Max(0, removedCount),
            keptCount,
            true,
            message);
    }

    private static byte[] ScrubPdfData(byte[] inputBytes)
    {
        var rawText = Encoding.Latin1.GetString(inputBytes);

        // 1. Scrub /Info object dictionary
        // Find /Info N 0 R in trailer
        var trailerInfoMatches = Regex.Matches(rawText, @"/Info\s+(?<num>\d+)\s+(?<gen>\d+)\s+R", RegexOptions.IgnoreCase);
        foreach (Match match in trailerInfoMatches)
        {
            var objNum = match.Groups["num"].Value;
            var genNum = match.Groups["gen"].Value;

            // Replace the body of obj N gen << ... >> endobj with obj N gen << >> endobj
            var objPattern = $@"\b{objNum}\s+{genNum}\s+obj\s*<<.*?>>\s*endobj";
            rawText = Regex.Replace(rawText, objPattern, $"{objNum} {genNum} obj\n<<\n>>\nendobj", RegexOptions.Singleline);
        }

        // Also empty any standalone Info dictionaries containing standard metadata keys
        var standaloneInfoRegex = new Regex(@"\b(?<header>\d+\s+\d+\s+obj)\s*<<(?<content>[^>]*?(?:/Title|/Author|/Creator|/Producer|/CreationDate)[^>]*?)>>\s*endobj", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        rawText = standaloneInfoRegex.Replace(rawText, "${header}\n<<\n>>\nendobj");

        // 2. Remove /Metadata N 0 R from Catalog dictionary if present
        rawText = Regex.Replace(rawText, @"/Metadata\s+\d+\s+\d+\s+R", "", RegexOptions.IgnoreCase);

        // 3. Scrub XMP streams: replace <?xpacket begin ... <?xpacket end ... ?> or <x:xmpmeta ... </x:xmpmeta>
        rawText = Regex.Replace(rawText, @"<\?xpacket begin.*?<\?xpacket end[^>]*\?>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        rawText = Regex.Replace(rawText, @"<x:xmpmeta.*?</x:xmpmeta>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return Encoding.Latin1.GetBytes(rawText);
    }
}
