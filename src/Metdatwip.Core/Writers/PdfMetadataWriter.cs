using System.Text;
using System.Text.RegularExpressions;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;

namespace Metdatwip.Core.Writers;

/// <summary>
/// Writes metadata edits to PDF documents by updating or injecting the Document Information Dictionary (/Info).
/// </summary>
public sealed class PdfMetadataWriter : IMetadataWriter
{
    private static readonly HashSet<string> SupportedExtensions = [".pdf"];
    private readonly PdfMetadataReader _reader;

    public PdfMetadataWriter(ISensitivityClassifier sensitivityClassifier)
    {
        ArgumentNullException.ThrowIfNull(sensitivityClassifier);
        _reader = new PdfMetadataReader(sensitivityClassifier);
    }

    /// <inheritdoc />
    public string Name => "pdf-metadata-writer";

    /// <inheritdoc />
    public bool CanWrite(string filePath, byte[]? magicBytes = null) =>
        _reader.CanRead(filePath, magicBytes);

    /// <inheritdoc />
    public async Task<WriteResult> WriteAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<MetadataEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(edits);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("PDF file not found.", inputPath);
        }

        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        var isSameFile = string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        var targetFile = isSameFile ? Path.Combine(Path.GetTempPath(), "metdatwip_edit_pdf_" + Guid.NewGuid().ToString("N") + ".pdf") : outputFullPath;

        var inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
        var resultBytes = WritePdfMetadata(inputBytes, edits);

        var outputDir = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await File.WriteAllBytesAsync(targetFile, resultBytes, cancellationToken);

        if (isSameFile)
        {
            File.Move(targetFile, outputFullPath, overwrite: true);
        }

        // Verification scan
        var afterDoc = await _reader.ReadAsync(outputFullPath, cancellationToken);
        var verifiedCount = 0;

        foreach (var edit in edits)
        {
            var match = afterDoc.Fields.FirstOrDefault(f =>
                f.Name.Equals(edit.Name, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(MapToStandardKey(edit.Name), StringComparison.OrdinalIgnoreCase));

            if (match is not null && match.Value.Contains(edit.NewValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                verifiedCount++;
            }
        }

        var message = $"Applied {edits.Count} edit(s). Verification: {verifiedCount}/{edits.Count} edits confirmed in output PDF.";
        return new WriteResult(inputPath, outputFullPath, edits.Count, 0, true, message);
    }

    private static byte[] WritePdfMetadata(byte[] inputBytes, IReadOnlyList<MetadataEdit> edits)
    {
        var rawText = Encoding.Latin1.GetString(inputBytes);

        // Normalize edits into dictionary keys
        var dictEntries = new StringBuilder();
        foreach (var edit in edits)
        {
            var key = MapToStandardKey(edit.Name);
            var escapedVal = EscapePdfString(edit.NewValue);
            dictEntries.AppendLine($"  /{key} ({escapedVal})");
        }

        // Check if an /Info N 0 R object is already referenced
        var trailerInfoMatch = Regex.Match(rawText, @"/Info\s+(?<num>\d+)\s+(?<gen>\d+)\s+R", RegexOptions.IgnoreCase);
        if (trailerInfoMatch.Success)
        {
            var objNum = trailerInfoMatch.Groups["num"].Value;
            var genNum = trailerInfoMatch.Groups["gen"].Value;

            var objPattern = $@"\b{objNum}\s+{genNum}\s+obj\s*<<.*?>>\s*endobj";
            var newObj = $"{objNum} {genNum} obj\n<<\n{dictEntries}>>\nendobj";

            if (Regex.IsMatch(rawText, objPattern, RegexOptions.Singleline))
            {
                rawText = Regex.Replace(rawText, objPattern, newObj, RegexOptions.Singleline);
                return Encoding.Latin1.GetBytes(rawText);
            }
        }

        // Otherwise find the highest object number and append a new /Info object
        var maxObjNum = 1;
        var objMatches = Regex.Matches(rawText, @"\b(?<num>\d+)\s+\d+\s+obj");
        foreach (Match match in objMatches)
        {
            if (int.TryParse(match.Groups["num"].Value, out var n) && n > maxObjNum)
            {
                maxObjNum = n;
            }
        }

        var newInfoObjNum = maxObjNum + 1;
        var newInfoObject = $"\n{newInfoObjNum} 0 obj\n<<\n{dictEntries}>>\nendobj\n";

        // Inject /Info newInfoObjNum 0 R into trailer
        var trailerMatch = Regex.Match(rawText, @"trailer\s*<<(?<content>.*?)>>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (trailerMatch.Success)
        {
            var trailerContent = trailerMatch.Groups["content"].Value;
            // If already has /Info, replace it; otherwise add it
            string newTrailerContent;
            if (Regex.IsMatch(trailerContent, @"/Info\s+\d+\s+\d+\s+R", RegexOptions.IgnoreCase))
            {
                newTrailerContent = Regex.Replace(trailerContent, @"/Info\s+\d+\s+\d+\s+R", $"/Info {newInfoObjNum} 0 R", RegexOptions.IgnoreCase);
            }
            else
            {
                newTrailerContent = $"{trailerContent}\n  /Info {newInfoObjNum} 0 R";
            }

            var newTrailer = $"trailer\n<<{newTrailerContent}\n>>";
            var trailerIndex = trailerMatch.Index;

            rawText = rawText.Substring(0, trailerIndex) + newInfoObject + newTrailer + rawText.Substring(trailerIndex + trailerMatch.Length);
        }
        else
        {
            // If no explicit trailer keyword, append before %%EOF
            var eofIndex = rawText.LastIndexOf("%%EOF", StringComparison.OrdinalIgnoreCase);
            if (eofIndex >= 0)
            {
                rawText = rawText.Substring(0, eofIndex) + newInfoObject + rawText.Substring(eofIndex);
            }
            else
            {
                rawText += newInfoObject;
            }
        }

        return Encoding.Latin1.GetBytes(rawText);
    }

    private static string MapToStandardKey(string name) => name.ToLowerInvariant() switch
    {
        "title" => "Title",
        "author" or "creator" => "Author",
        "subject" => "Subject",
        "keywords" => "Keywords",
        "software" or "tool" or "creatortool" => "Creator",
        "producer" => "Producer",
        "creationdate" or "date" or "createdate" => "CreationDate",
        "moddate" or "modifydate" => "ModDate",
        "company" => "Company",
        _ => char.ToUpperInvariant(name[0]) + name[1..],
    };

    private static string EscapePdfString(string value) =>
        value.Replace("\\", "\\\\")
             .Replace("(", "\\(")
             .Replace(")", "\\)");
}
