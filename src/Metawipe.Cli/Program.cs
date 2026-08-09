using System.Text.Json;
using Metawipe.Core.Classification;
using Metawipe.Core.Models;
using Metawipe.Core.Readers;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].Trim().ToLowerInvariant();
if (command != "inspect")
{
    Console.Error.WriteLine($"Unknown command: {args[0]}");
    PrintUsage();
    return 1;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Missing file path for inspect command.");
    PrintUsage();
    return 1;
}

var targetPath = args[1];
var jsonOutput = args.Skip(2).Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));

if (!File.Exists(targetPath))
{
    Console.Error.WriteLine($"File not found: {targetPath}");
    return 2;
}

var classifier = new RuleBasedSensitivityClassifier();
var reader = new ImageMetadataReader(classifier);
var magicBytes = ReadLeadingBytes(targetPath, 16);

if (!reader.CanRead(targetPath, magicBytes))
{
    Console.Error.WriteLine("Unsupported file type for inspect. Supported: JPEG, PNG, TIFF, HEIC/HEIF, WebP.");
    return 3;
}

var document = await reader.ReadAsync(targetPath);
var report = BuildReport(document);

if (jsonOutput)
{
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true,
    }));

    return 0;
}

PrintHumanReadable(report);
return 0;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  metawipe inspect <file> [--json]");
}

static byte[] ReadLeadingBytes(string path, int count)
{
    using var stream = File.OpenRead(path);
    var buffer = new byte[count];
    var bytesRead = stream.Read(buffer, 0, count);
    if (bytesRead == count)
    {
        return buffer;
    }

    return buffer.Take(bytesRead).ToArray();
}

static InspectReport BuildReport(MetadataDocument document)
{
    var grouped = document.GroupedFields
        .Select(group => new MetadataGroupReport(
            group.Key,
            group.Select(field => new MetadataFieldReport(
                field.Name,
                field.Value,
                field.IsSensitive,
                field.Removable)).ToList()))
        .ToList();

    var total = document.Fields.Count;
    var sensitive = document.Fields.Count(field => field.IsSensitive);

    return new InspectReport(
        Path.GetFullPath(document.SourcePath),
        total,
        sensitive,
        grouped);
}

static void PrintHumanReadable(InspectReport report)
{
    Console.WriteLine($"Source: {report.SourcePath}");
    Console.WriteLine($"Total fields: {report.TotalFields}");
    Console.WriteLine($"Sensitive fields: {report.SensitiveFields}");

    if (report.Groups.Count == 0)
    {
        Console.WriteLine("No metadata fields found.");
        return;
    }

    foreach (var group in report.Groups)
    {
        Console.WriteLine();
        Console.WriteLine($"[{group.Group}]");
        foreach (var field in group.Fields)
        {
            var marker = field.IsSensitive ? " [SENSITIVE]" : string.Empty;
            Console.WriteLine($"- {field.Name}: {field.Value}{marker}");
        }
    }
}

internal sealed record InspectReport(
    string SourcePath,
    int TotalFields,
    int SensitiveFields,
    IReadOnlyList<MetadataGroupReport> Groups);

internal sealed record MetadataGroupReport(
    string Group,
    IReadOnlyList<MetadataFieldReport> Fields);

internal sealed record MetadataFieldReport(
    string Name,
    string Value,
    bool IsSensitive,
    bool Removable);
