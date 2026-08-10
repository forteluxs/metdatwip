using System.Text.Json;
using Metawipe.Core.Classification;
using Metawipe.Core.Models;
using Metawipe.Core.Readers;
using Metawipe.Core.Scrubbers;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].Trim().ToLowerInvariant();
return command switch
{
    "inspect" => await RunInspectAsync(args),
    "scrub" => await RunScrubAsync(args),
    _ => UnknownCommand(args[0]),
};

static int UnknownCommand(string rawCommand)
{
    Console.Error.WriteLine($"Unknown command: {rawCommand}");
    PrintUsage();
    return 1;
}

static async Task<int> RunInspectAsync(string[] args)
{
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
    var report = BuildInspectReport(document);

    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        return 0;
    }

    PrintHumanReadableInspect(report);
    return 0;
}

static async Task<int> RunScrubAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Missing target path for scrub command.");
        PrintUsage();
        return 1;
    }

    var targetPath = args[1];
    var optionsResult = ParseScrubOptions(args.Skip(2).ToArray());
    if (!optionsResult.Success)
    {
        Console.Error.WriteLine(optionsResult.ErrorMessage);
        PrintUsage();
        return 1;
    }

    var options = optionsResult.Options!;
    var classifier = new RuleBasedSensitivityClassifier();
    var reader = new ImageMetadataReader(classifier);
    var scrubber = new ImageMetadataScrubber(classifier);

    if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
    {
        Console.Error.WriteLine($"Target path not found: {targetPath}");
        return 2;
    }

    var candidateFiles = ResolveCandidateFiles(targetPath, options.Recursive);
    if (candidateFiles.Count == 0)
    {
        Console.Error.WriteLine("No files found to scrub.");
        return 3;
    }

    var processedCount = 0;
    var skippedCount = 0;
    var failedCount = 0;
    var totalSensitiveRemaining = 0;

    foreach (var inputPath in candidateFiles)
    {
        var magicBytes = ReadLeadingBytes(inputPath, 16);
        if (!scrubber.CanScrub(inputPath, magicBytes))
        {
            skippedCount++;
            continue;
        }

        var outputPath = BuildOutputPath(inputPath, targetPath, options.OutputDirectory);

        if (options.DryRun)
        {
            var inspection = await reader.ReadAsync(inputPath);
            var removableFields = inspection.Fields.Where(field => field.Removable).ToList();
            var removeCount = removableFields.Count(field => options.Profile.ShouldRemove(field));
            var keepCount = removableFields.Count - removeCount;

            Console.WriteLine($"DRY-RUN {inputPath}");
            Console.WriteLine($"  output: {outputPath}");
            Console.WriteLine($"  removable fields: {removableFields.Count}, would remove: {removeCount}, would keep: {keepCount}");
            processedCount++;
            continue;
        }

        try
        {
            var result = await scrubber.ScrubAsync(inputPath, outputPath, options.Profile);
            var verifyDocument = await reader.ReadAsync(outputPath);
            var sensitiveRemaining = verifyDocument.Fields.Count(field => field.IsSensitive);
            totalSensitiveRemaining += sensitiveRemaining;

            Console.WriteLine($"SCRUBBED {inputPath}");
            Console.WriteLine($"  output: {result.OutputPath}");
            Console.WriteLine($"  removed fields: {result.RemovedFields}, kept fields: {result.KeptFields}");
            Console.WriteLine($"  verify sensitive remaining: {sensitiveRemaining}");

            processedCount++;
        }
        catch (Exception ex)
        {
            failedCount++;
            Console.Error.WriteLine($"FAILED {inputPath}: {ex.Message}");
        }
    }

    if (processedCount == 0 && skippedCount > 0)
    {
        Console.Error.WriteLine("No supported files found. Supported scrub formats: JPEG, PNG.");
        return 3;
    }

    if (options.DryRun)
    {
        Console.WriteLine();
        Console.WriteLine($"Dry-run complete. Planned: {processedCount}, Skipped unsupported: {skippedCount}, Failed: {failedCount}");
        return failedCount == 0 ? 0 : 4;
    }

    Console.WriteLine();
    Console.WriteLine($"Scrub complete. Processed: {processedCount}, Skipped unsupported: {skippedCount}, Failed: {failedCount}, Sensitive fields remaining: {totalSensitiveRemaining}");
    return failedCount == 0 ? 0 : 4;
}

static ParseScrubOptionsResult ParseScrubOptions(string[] args)
{
    var recursive = false;
    var dryRun = false;
    string? outputDirectory = null;
    string[]? keepFields = null;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i].Trim();
        switch (arg)
        {
            case "--recursive":
                recursive = true;
                break;

            case "--dry-run":
                dryRun = true;
                break;

            case "--out":
                if (i + 1 >= args.Length)
                {
                    return ParseScrubOptionsResult.Fail("--out requires a directory path.");
                }

                outputDirectory = args[++i].Trim();
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    return ParseScrubOptionsResult.Fail("--out requires a non-empty directory path.");
                }

                break;

            case "--keep":
                if (i + 1 >= args.Length)
                {
                    return ParseScrubOptionsResult.Fail("--keep requires a comma-separated field list.");
                }

                keepFields = args[++i]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (keepFields.Length == 0)
                {
                    return ParseScrubOptionsResult.Fail("--keep requires at least one field.");
                }

                break;

            default:
                return ParseScrubOptionsResult.Fail($"Unknown scrub option: {arg}");
        }
    }

    var profile = keepFields is { Length: > 0 }
        ? ScrubProfile.CreateKeepWhitelist(keepFields.Select(NormalizeKeepField))
        : ScrubProfile.CreateStripAll();

    return ParseScrubOptionsResult.Ok(new ScrubOptions(recursive, dryRun, outputDirectory, profile));
}

static string NormalizeKeepField(string raw)
{
    var token = raw.Trim().ToLowerInvariant();
    if (token.Contains('/'))
    {
        return token;
    }

    return token switch
    {
        "orientation" => "exif/orientation",
        "icc" or "icc-profile" or "icc_profile" => "icc/profile",
        _ => $"exif/{token.Replace('_', ' ').Replace('-', ' ')}",
    };
}

static List<string> ResolveCandidateFiles(string targetPath, bool recursive)
{
    if (File.Exists(targetPath))
    {
        return
        [
            Path.GetFullPath(targetPath),
        ];
    }

    if (!Directory.Exists(targetPath))
    {
        return [];
    }

    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
    return Directory
        .EnumerateFiles(targetPath, "*", searchOption)
        .Where(path => !Path.GetFileName(path).Contains(".cleaned.", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFullPath)
        .ToList();
}

static string BuildOutputPath(string inputPath, string originalTargetPath, string? outputDirectory)
{
    var fileName = Path.GetFileNameWithoutExtension(inputPath);
    var extension = Path.GetExtension(inputPath);
    var cleanedFileName = $"{fileName}.cleaned{extension}";

    if (string.IsNullOrWhiteSpace(outputDirectory))
    {
        var inputDirectory = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(inputDirectory, cleanedFileName);
    }

    var outputRoot = Path.GetFullPath(outputDirectory);

    if (File.Exists(originalTargetPath))
    {
        return Path.Combine(outputRoot, cleanedFileName);
    }

    var sourceRoot = Path.GetFullPath(originalTargetPath);
    var relativePath = Path.GetRelativePath(sourceRoot, inputPath);
    var relativeDirectory = Path.GetDirectoryName(relativePath);

    return string.IsNullOrWhiteSpace(relativeDirectory)
        ? Path.Combine(outputRoot, cleanedFileName)
        : Path.Combine(outputRoot, relativeDirectory, cleanedFileName);
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  metawipe inspect <file> [--json]");
    Console.WriteLine("  metawipe scrub <file|folder> [--recursive] [--dry-run] [--out DIR] [--keep field1,field2]");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  metawipe scrub ./photo.jpg");
    Console.WriteLine("  metawipe scrub ./Exports --recursive --dry-run");
    Console.WriteLine("  metawipe scrub ./photo.jpg --keep orientation,icc-profile");
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

static InspectReport BuildInspectReport(MetadataDocument document)
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

static void PrintHumanReadableInspect(InspectReport report)
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

internal sealed record ScrubOptions(
    bool Recursive,
    bool DryRun,
    string? OutputDirectory,
    ScrubProfile Profile);

internal sealed record ParseScrubOptionsResult(bool Success, ScrubOptions? Options, string? ErrorMessage)
{
    public static ParseScrubOptionsResult Ok(ScrubOptions options) => new(true, options, null);

    public static ParseScrubOptionsResult Fail(string message) => new(false, null, message);
}
