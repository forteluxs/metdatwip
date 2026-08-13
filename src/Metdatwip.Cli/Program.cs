using System.Text.Json;
using Metdatwip.Core.Abstractions;
using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Readers;
using Metdatwip.Core.Routing;
using Metdatwip.Core.Scrubbers;
using Metdatwip.Core.Writers;

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
    "edit" => await RunEditAsync(args),
    "randomize" => await RunRandomizeAsync(args),
    "version" or "--version" or "-v" => PrintVersion(),
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
    var router = CreateFormatRouter(classifier);
    var magicBytes = ReadLeadingBytes(targetPath, 16);

    var route = router.ResolveReader(targetPath, magicBytes);
    if (!route.IsSupported || route.Handler is null)
    {
        Console.Error.WriteLine(route.Message);
        Console.Error.WriteLine("Supported inspect formats: JPEG, PNG, TIFF, HEIC/HEIF, WebP, DOCX, XLSX, PPTX.");
        return 3;
    }

    var document = await route.Handler.ReadAsync(targetPath);
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
    var router = CreateFormatRouter(classifier);

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
        var scrubberRoute = router.ResolveScrubber(inputPath, magicBytes);
        if (!scrubberRoute.IsSupported || scrubberRoute.Handler is null)
        {
            skippedCount++;
            continue;
        }

        var readerRoute = router.ResolveReader(inputPath, magicBytes);
        if (!readerRoute.IsSupported || readerRoute.Handler is null)
        {
            failedCount++;
            Console.Error.WriteLine($"FAILED {inputPath}: {readerRoute.Message}");
            continue;
        }

        var outputPath = BuildOutputPath(inputPath, targetPath, options.OutputDirectory);

        if (options.DryRun)
        {
            var inspection = await readerRoute.Handler.ReadAsync(inputPath);
            var removableFields = inspection.Fields.Where(field => field.Removable).ToList();
            var removeCount = removableFields.Count(field => options.Profile.ShouldRemove(field));
            var keepCount = removableFields.Count - removeCount;

            Console.WriteLine($"DRY-RUN {inputPath}");
            Console.WriteLine($"  format: {scrubberRoute.FormatName}");
            Console.WriteLine($"  output: {outputPath}");
            Console.WriteLine($"  removable fields: {removableFields.Count}, would remove: {removeCount}, would keep: {keepCount}");
            processedCount++;
            continue;
        }

        try
        {
            var result = await scrubberRoute.Handler.ScrubAsync(inputPath, outputPath, options.Profile);

            var outputMagicBytes = ReadLeadingBytes(outputPath, 16);
            var verifyRoute = router.ResolveReader(outputPath, outputMagicBytes);
            var verifyReader = verifyRoute.IsSupported && verifyRoute.Handler is not null
                ? verifyRoute.Handler
                : readerRoute.Handler;

            var verifyDocument = await verifyReader.ReadAsync(outputPath);
            var sensitiveRemaining = verifyDocument.Fields.Count(field => field.IsSensitive);
            totalSensitiveRemaining += sensitiveRemaining;

            Console.WriteLine($"SCRUBBED {inputPath}");
            Console.WriteLine($"  format: {scrubberRoute.FormatName}");
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
        Console.Error.WriteLine("No supported files found. Supported scrub formats: JPEG, PNG, DOCX, XLSX, PPTX.");
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

static async Task<int> RunEditAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Missing file path for edit command.");
        PrintUsage();
        return 1;
    }

    var targetPath = args[1];

    if (!File.Exists(targetPath))
    {
        Console.Error.WriteLine($"File not found: {targetPath}");
        return 2;
    }

    // Parse --set and --out options
    var edits = new List<MetadataEdit>();
    string? outputFile = null;

    for (var i = 2; i < args.Length; i++)
    {
        var arg = args[i].Trim();
        switch (arg)
        {
            case "--set":
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--set requires a value in 'group/name=value' format.");
                    return 1;
                }

                try
                {
                    edits.Add(MetadataEdit.Parse(args[++i]));
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }

                break;

            case "--out":
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--out requires a file path.");
                    return 1;
                }

                outputFile = args[++i].Trim();
                break;

            default:
                Console.Error.WriteLine($"Unknown edit option: {arg}");
                PrintUsage();
                return 1;
        }
    }

    if (edits.Count == 0)
    {
        Console.Error.WriteLine("No edits specified. Use --set group/name=value to specify edits.");
        PrintUsage();
        return 1;
    }

    // Determine output path
    if (string.IsNullOrWhiteSpace(outputFile))
    {
        var fileName = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        var directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        outputFile = Path.Combine(directory, $"{fileName}.edited{extension}");
    }

    var classifier = new RuleBasedSensitivityClassifier();
    var router = CreateFormatRouter(classifier);
    var magicBytes = ReadLeadingBytes(targetPath, 16);

    var writerRoute = router.ResolveWriter(targetPath, magicBytes);
    if (!writerRoute.IsSupported || writerRoute.Handler is null)
    {
        Console.Error.WriteLine(writerRoute.Message);
        Console.Error.WriteLine("Supported edit formats: JPEG, PNG, DOCX, XLSX, PPTX.");
        return 3;
    }

    try
    {
        Console.WriteLine($"Editing: {targetPath}");
        Console.WriteLine($"Format:  {writerRoute.FormatName}");
        Console.WriteLine($"Output:  {outputFile}");
        Console.WriteLine($"Edits:   {edits.Count}");
        Console.WriteLine();

        foreach (var edit in edits)
        {
            Console.WriteLine($"  SET {edit.Group}/{edit.Name} = {edit.NewValue}");
        }

        Console.WriteLine();

        var result = await writerRoute.Handler.WriteAsync(targetPath, outputFile, edits);

        Console.WriteLine($"Result: {(result.IsSuccess ? "SUCCESS" : "FAILED")}");
        Console.WriteLine($"  Applied: {result.AppliedEdits}");
        Console.WriteLine($"  Skipped: {result.SkippedEdits}");

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            Console.WriteLine($"  {result.Message}");
        }

        // Run a verification inspect
        var readerRoute = router.ResolveReader(outputFile, ReadLeadingBytes(outputFile, 16));
        if (readerRoute.IsSupported && readerRoute.Handler is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Verification inspect of edited file:");
            var verifyDocument = await readerRoute.Handler.ReadAsync(outputFile);
            foreach (var group in verifyDocument.GroupedFields)
            {
                Console.WriteLine($"  [{group.Key}]");
                foreach (var field in group)
                {
                    var marker = field.IsSensitive ? " [SENSITIVE]" : string.Empty;
                    Console.WriteLine($"  - {field.Name}: {field.Value}{marker}");
                }
            }
        }

        return result.IsSuccess ? 0 : 4;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Edit failed: {ex.Message}");
        return 4;
    }
}

static async Task<int> RunRandomizeAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Missing file path for randomize command.");
        PrintUsage();
        return 1;
    }

    var targetPath = args[1];
    if (!File.Exists(targetPath))
    {
        Console.Error.WriteLine($"File not found: {targetPath}");
        return 2;
    }

    string? outputFile = null;
    for (var i = 2; i < args.Length; i++)
    {
        var arg = args[i].Trim();
        if (arg == "--out" && i + 1 < args.Length)
        {
            outputFile = args[++i].Trim();
        }
    }

    if (string.IsNullOrWhiteSpace(outputFile))
    {
        var fileName = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        var directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        outputFile = Path.Combine(directory, $"{fileName}.randomized{extension}");
    }

    var ext = Path.GetExtension(targetPath).ToLowerInvariant();
    List<MetadataEdit> edits;

    if (ext is ".jpg" or ".jpeg" or ".png")
    {
        edits = MetadataRandomizer.GenerateImageEdits();
    }
    else if (ext is ".docx" or ".xlsx" or ".pptx")
    {
        edits = MetadataRandomizer.GenerateOoxmlEdits();
    }
    else
    {
        Console.Error.WriteLine("Randomize is supported for JPEG, PNG, DOCX, XLSX, PPTX.");
        return 3;
    }

    var classifier = new RuleBasedSensitivityClassifier();
    var router = CreateFormatRouter(classifier);
    var magicBytes = ReadLeadingBytes(targetPath, 16);

    var writerRoute = router.ResolveWriter(targetPath, magicBytes);
    if (!writerRoute.IsSupported || writerRoute.Handler is null)
    {
        Console.Error.WriteLine(writerRoute.Message);
        return 3;
    }

    Console.WriteLine($"Randomizing metadata for: {targetPath}");
    Console.WriteLine($"Output:                   {outputFile}");
    Console.WriteLine();
    foreach (var edit in edits)
    {
        Console.WriteLine($"  SET {edit.Group}/{edit.Name} = {edit.NewValue}");
    }
    Console.WriteLine();

    var result = await writerRoute.Handler.WriteAsync(targetPath, outputFile, edits);
    Console.WriteLine($"Result: {(result.IsSuccess ? "SUCCESS" : "FAILED")}");
    Console.WriteLine($"  {result.Message}");

    return result.IsSuccess ? 0 : 4;
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

static FormatRouter CreateFormatRouter(ISensitivityClassifier classifier)
{
    var router = new FormatRouter();

    var imageReader = new ImageMetadataReader(classifier);
    var imageScrubber = new ImageMetadataScrubber(classifier);
    var imageWriter = new ImageMetadataWriter(classifier);
    var ooxmlReader = new OoxmlMetadataReader(classifier);
    var ooxmlScrubber = new OoxmlMetadataScrubber(classifier);
    var ooxmlWriter = new OoxmlMetadataWriter(classifier);

    router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
        "Image",
        imageReader,
        [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".heic", ".heif", ".webp"],
        MatchesImageMagic));

    router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
        "Image",
        imageScrubber,
        [".jpg", ".jpeg", ".png"],
        MatchesImageMagic));

    router.RegisterWriter(new FormatHandlerRegistration<IMetadataWriter>(
        "Image",
        imageWriter,
        [".jpg", ".jpeg", ".png"],
        MatchesImageMagic));

    router.RegisterReader(new FormatHandlerRegistration<IMetadataReader>(
        "OOXML",
        ooxmlReader,
        [".docx", ".xlsx", ".pptx"],
        MatchesZipMagic));

    router.RegisterScrubber(new FormatHandlerRegistration<IMetadataScrubber>(
        "OOXML",
        ooxmlScrubber,
        [".docx", ".xlsx", ".pptx"],
        MatchesZipMagic));

    router.RegisterWriter(new FormatHandlerRegistration<IMetadataWriter>(
        "OOXML",
        ooxmlWriter,
        [".docx", ".xlsx", ".pptx"],
        MatchesZipMagic));

    return router;
}

static bool MatchesImageMagic(byte[] bytes)
{
    // JPEG
    if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
    {
        return true;
    }

    // PNG
    if (bytes.Length >= 8 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
    {
        return true;
    }

    // TIFF (little- or big-endian)
    if (bytes.Length >= 4 &&
        ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
         (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
    {
        return true;
    }

    // WEBP container: RIFF....WEBP
    if (bytes.Length >= 12 &&
        bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
        bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
    {
        return true;
    }

    // HEIF/HEIC ISO BMFF
    if (bytes.Length >= 12 &&
        bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
    {
        var brand = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
        return brand is "heic" or "heif" or "heix" or "hevc" or "hevx" or "mif1" or "msf1";
    }

    return false;
}

static bool MatchesZipMagic(byte[] bytes) =>
    bytes.Length >= 4 &&
    bytes[0] == (byte)'P' &&
    bytes[1] == (byte)'K' &&
    bytes[2] is 0x03 or 0x05 or 0x07 &&
    bytes[3] is 0x04 or 0x06 or 0x08;

static void PrintUsage()
{
    Console.WriteLine("metdatwip v1.1.0 — Privacy & Metadata Editor");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  metdatwip inspect <file> [--json]");
    Console.WriteLine("  metdatwip scrub <file|folder> [--recursive] [--dry-run] [--out DIR] [--keep field1,field2]");
    Console.WriteLine("  metdatwip edit <file> --set group/name=value [--set ...] [--out FILE]");
    Console.WriteLine("  metdatwip randomize <file> [--out FILE]");
    Console.WriteLine("  metdatwip version");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  metdatwip inspect ./photo.jpg");
    Console.WriteLine("  metdatwip inspect ./report.docx --json");
    Console.WriteLine("  metdatwip scrub ./photo.jpg");
    Console.WriteLine("  metdatwip scrub ./Exports --recursive --dry-run");
    Console.WriteLine("  metdatwip scrub ./photo.jpg --keep orientation,icc-profile");
    Console.WriteLine("  metdatwip edit ./photo.jpg --set exif/artist=John --set exif/copyright=2024");
    Console.WriteLine("  metdatwip randomize ./photo.jpg --out ./randomized.jpg");
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

static int PrintVersion()
{
    Console.WriteLine("metdatwip v1.1.0");
    return 0;
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
