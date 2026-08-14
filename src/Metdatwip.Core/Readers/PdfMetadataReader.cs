using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Models;

namespace Metdatwip.Core.Readers;

/// <summary>
/// Reads metadata from PDF documents by inspecting the Document Information Dictionary (/Info)
/// and Document Catalog XMP Metadata Streams (/Metadata).
/// </summary>
public sealed class PdfMetadataReader : IMetadataReader
{
    private static readonly HashSet<string> SupportedExtensions = [".pdf"];
    private readonly ISensitivityClassifier _sensitivityClassifier;

    public PdfMetadataReader(ISensitivityClassifier sensitivityClassifier)
    {
        _sensitivityClassifier = sensitivityClassifier ?? throw new ArgumentNullException(nameof(sensitivityClassifier));
    }

    /// <inheritdoc />
    public string Name => "pdf-metadata-reader";

    /// <inheritdoc />
    public bool CanRead(string filePath, byte[]? magicBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension.ToLowerInvariant()))
        {
            return true;
        }

        return MatchesPdfMagic(magicBytes);
    }

    /// <inheritdoc />
    public async Task<MetadataDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("PDF file not found.", filePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var document = new MetadataDocument(filePath);

        ReadInfoDictionary(bytes, document);
        ReadXmpMetadata(bytes, document);

        return document;
    }

    private void ReadInfoDictionary(byte[] bytes, MetadataDocument document)
    {
        var rawText = Encoding.Latin1.GetString(bytes);

        // Locate /Info dictionary: either via trailer /Info N 0 R or direct /Info << ... >> or standalone obj << ... /Title ... >>
        var infoDicts = FindInfoDictionaries(rawText);

        foreach (var dictContent in infoDicts)
        {
            var entries = ParseDictionaryEntries(dictContent);
            foreach (var (key, value) in entries)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    AddField(document, "PDF-Info", key, value);
                }
            }
        }
    }

    private void ReadXmpMetadata(byte[] bytes, MetadataDocument document)
    {
        var rawText = Encoding.Latin1.GetString(bytes);

        var xmpStart = rawText.IndexOf("<x:xmpmeta", StringComparison.OrdinalIgnoreCase);
        if (xmpStart < 0)
        {
            xmpStart = rawText.IndexOf("<?xpacket begin", StringComparison.OrdinalIgnoreCase);
        }

        if (xmpStart < 0) return;

        var xmpEnd = rawText.IndexOf("</x:xmpmeta>", xmpStart, StringComparison.OrdinalIgnoreCase);
        if (xmpEnd >= 0)
        {
            xmpEnd += "</x:xmpmeta>".Length;
        }
        else
        {
            var packetEnd = rawText.IndexOf("<?xpacket end", xmpStart, StringComparison.OrdinalIgnoreCase);
            if (packetEnd >= 0)
            {
                xmpEnd = rawText.IndexOf('>', packetEnd);
                if (xmpEnd >= 0) xmpEnd += 1;
            }
        }

        if (xmpEnd <= xmpStart) return;

        var xmpXml = rawText.Substring(xmpStart, xmpEnd - xmpStart);

        try
        {
            var xdoc = XDocument.Parse(xmpXml);
            foreach (var element in xdoc.Descendants())
            {
                var localName = element.Name.LocalName;
                if (localName is "title" or "creator" or "description" or "subject" or "format" or
                    "CreatorTool" or "CreateDate" or "ModifyDate" or "MetadataDate" or "Producer" or
                    "Keywords" or "Author" or "Company" or "DocumentID" or "InstanceID")
                {
                    var text = element.Value.Trim();
                    if (!string.IsNullOrWhiteSpace(text) && !element.HasElements)
                    {
                        var group = element.Name.NamespaceName.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? "PDF-XMP" : "XMP";
                        AddField(document, group, localName, text);
                    }
                }
            }
        }
        catch
        {
            // Fallback regex if XML is slightly malformed
            var tags = new[] { "title", "creator", "description", "CreatorTool", "CreateDate", "ModifyDate", "Producer" };
            foreach (var tag in tags)
            {
                var match = Regex.Match(xmpXml, $@"<{tag}[^>]*>([^<]+)</{tag}>", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    AddField(document, "XMP", tag, match.Groups[1].Value.Trim());
                }
            }
        }
    }

    private static List<string> FindInfoDictionaries(string rawText)
    {
        var result = new List<string>();

        // 1. Look for trailer /Info N 0 R
        var trailerMatches = Regex.Matches(rawText, @"/Info\s+(\d+)\s+(\d+)\s+R", RegexOptions.IgnoreCase);
        foreach (Match match in trailerMatches)
        {
            var objNum = match.Groups[1].Value;
            var genNum = match.Groups[2].Value;

            var objRegex = new Regex($@"\b{objNum}\s+{genNum}\s+obj\s*<<(?<content>.*?)>>\s*endobj", RegexOptions.Singleline);
            var objMatch = objRegex.Match(rawText);
            if (objMatch.Success)
            {
                result.Add(objMatch.Groups["content"].Value);
            }
        }

        // 2. Fallback: match any obj with standard PDF info keys (Title, Author, Creator, Producer, CreationDate)
        if (result.Count == 0)
        {
            var genericObjRegex = new Regex(@"\b\d+\s+\d+\s+obj\s*<<(?<content>[^>]*?(?:/Title|/Author|/Creator|/Producer|/CreationDate)[^>]*?)>>\s*endobj", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match match in genericObjRegex.Matches(rawText))
            {
                result.Add(match.Groups["content"].Value);
            }
        }

        return result;
    }

    private static Dictionary<string, string> ParseDictionaryEntries(string dictContent)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Match /Key (StringLiteral) or /Key <HexLiteral> or /Key /Name or /Key (D:Date...)
        var pattern = @"/(?<key>[A-Za-z0-9_]+)\s*(?:\((?<str>(?:\\\(|\\\)|\\|[^)])*)\)|<(?<hex>[0-9A-Fa-f\s]+)>|/(?<name>[A-Za-z0-9_]+))";
        var matches = Regex.Matches(dictContent, pattern);

        foreach (Match match in matches)
        {
            var key = match.Groups["key"].Value;
            if (key is "Type" or "Filter" or "Length" or "Size") continue;

            string value;
            if (match.Groups["str"].Success)
            {
                value = DecodePdfStringLiteral(match.Groups["str"].Value);
            }
            else if (match.Groups["hex"].Success)
            {
                value = DecodePdfHexString(match.Groups["hex"].Value);
            }
            else if (match.Groups["name"].Success)
            {
                value = match.Groups["name"].Value;
            }
            else
            {
                continue;
            }

            entries[key] = value.Trim();
        }

        return entries;
    }

    public static string DecodePdfStringLiteral(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                var next = raw[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '(': sb.Append('('); break;
                    case ')': sb.Append(')'); break;
                    case '\\': sb.Append('\\'); break;
                    default:
                        if (next >= '0' && next <= '7')
                        {
                            var octal = "" + next;
                            if (i + 1 < raw.Length && raw[i + 1] >= '0' && raw[i + 1] <= '7') octal += raw[++i];
                            if (i + 1 < raw.Length && raw[i + 1] >= '0' && raw[i + 1] <= '7') octal += raw[++i];
                            sb.Append((char)Convert.ToInt32(octal, 8));
                        }
                        else
                        {
                            sb.Append(next);
                        }
                        break;
                }
            }
            else
            {
                sb.Append(raw[i]);
            }
        }

        var decoded = sb.ToString();

        // Check for UTF-16BE BOM (\xFE\xFF)
        if (decoded.Length >= 2 && decoded[0] == '\xFE' && decoded[1] == '\xFF')
        {
            var bytes = Encoding.Latin1.GetBytes(decoded[2..]);
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        return decoded;
    }

    public static string DecodePdfHexString(string hex)
    {
        var cleanHex = Regex.Replace(hex, @"\s+", "");
        if (cleanHex.Length % 2 != 0) cleanHex += "0";

        var bytes = new byte[cleanHex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private void AddField(MetadataDocument document, string group, string name, string value)
    {
        var tentative = new MetadataField(group, name, value, false, true);
        var isSensitive = _sensitivityClassifier.IsSensitive(tentative);
        document.AddField(tentative with { IsSensitive = isSensitive });
    }

    public static bool MatchesPdfMagic(byte[]? magicBytes) =>
        magicBytes is not null &&
        magicBytes.Length >= 5 &&
        magicBytes[0] == 0x25 && // '%'
        magicBytes[1] == 0x50 && // 'P'
        magicBytes[2] == 0x44 && // 'D'
        magicBytes[3] == 0x46 && // 'F'
        magicBytes[4] == 0x2D;   // '-'
}
