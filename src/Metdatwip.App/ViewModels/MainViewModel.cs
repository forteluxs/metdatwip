using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Metdatwip.App.Services;
using Metdatwip.Core.Classification;
using Metdatwip.Core.Models;
using Metdatwip.Core.Routing;
using Metdatwip.Core.Scrubbers;
using Metdatwip.Core.Writers;

namespace Metdatwip.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MetadataInspectionService _inspectionService = new();
    private readonly FormatRouter _router;

    [ObservableProperty]
    private string statusMessage = "Drop a file, browse, or enter a file path to inspect metadata.";

    [ObservableProperty]
    private string sourcePath = "No file selected.";

    [ObservableProperty]
    private string inputFilePath = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasFileLoaded;

    [ObservableProperty]
    private bool overwriteOriginalFile = true;

    public ObservableCollection<MetadataGroupViewModel> MetadataGroups { get; } = [];

    public MainViewModel()
    {
        var classifier = new RuleBasedSensitivityClassifier();
        _router = new FormatRouter();

        _router.RegisterScrubber(new FormatHandlerRegistration<Metdatwip.Core.Abstractions.IMetadataScrubber>(
            "Image", new ImageMetadataScrubber(classifier), [".jpg", ".jpeg", ".png"]));
        _router.RegisterScrubber(new FormatHandlerRegistration<Metdatwip.Core.Abstractions.IMetadataScrubber>(
            "OOXML", new OoxmlMetadataScrubber(classifier), [".docx", ".xlsx", ".pptx"]));

        _router.RegisterWriter(new FormatHandlerRegistration<Metdatwip.Core.Abstractions.IMetadataWriter>(
            "Image", new ImageMetadataWriter(classifier), [".jpg", ".jpeg", ".png"]));
        _router.RegisterWriter(new FormatHandlerRegistration<Metdatwip.Core.Abstractions.IMetadataWriter>(
            "OOXML", new OoxmlMetadataWriter(classifier), [".docx", ".xlsx", ".pptx"]));
    }

    [RelayCommand]
    private async Task InspectInputPathAsync()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath))
        {
            ErrorMessage = "Please enter a valid file path.";
            return;
        }

        var cleanPath = InputFilePath.Trim().Trim('\'', '"');
        if (!File.Exists(cleanPath) && !Directory.Exists(cleanPath))
        {
            ErrorMessage = $"File or folder not found: {cleanPath}";
            return;
        }

        await HandleDroppedPathsAsync([cleanPath]);
    }

    public async Task HandleDroppedPathsAsync(IEnumerable<string> droppedPaths)
    {
        ArgumentNullException.ThrowIfNull(droppedPaths);

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _inspectionService.InspectAsync(droppedPaths);

            SourcePath = result.SourcePath;
            InputFilePath = result.SourcePath;
            HasFileLoaded = File.Exists(SourcePath);
            RebuildGroups(result);

            var totalFields = result.Document.Fields.Count;
            var sensitiveFields = result.Document.Fields.Count(field => field.IsSensitive);
            var statusPrefix = result.WasFolderInput ? "Folder loaded. " : string.Empty;
            var note = string.IsNullOrWhiteSpace(result.Note) ? string.Empty : $" {result.Note}";

            StatusMessage =
                $"{statusPrefix}Inspected {Path.GetFileName(result.SourcePath)} — {totalFields} fields ({sensitiveFields} sensitive).{note}";
        }
        catch (Exception ex)
        {
            MetadataGroups.Clear();
            SourcePath = "No file selected.";
            HasFileLoaded = false;
            StatusMessage = "Could not inspect dropped items.";
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PopulateRandomMetadata()
    {
        if (!File.Exists(SourcePath)) return;

        var ext = Path.GetExtension(SourcePath).ToLowerInvariant();
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
            ErrorMessage = "Random metadata generation is supported for JPEG, PNG, DOCX, XLSX, PPTX.";
            return;
        }

        var populatedCount = 0;

        foreach (var edit in edits)
        {
            var groupVm = MetadataGroups.FirstOrDefault(g => g.GroupName.Equals(edit.Group, StringComparison.OrdinalIgnoreCase));
            if (groupVm is null)
            {
                var newField = new MetadataFieldViewModel(edit.Group, edit.Name, edit.NewValue, true, originalValue: string.Empty);
                var fields = new List<MetadataFieldViewModel> { newField };
                groupVm = new MetadataGroupViewModel(edit.Group, fields);
                MetadataGroups.Add(groupVm);
                populatedCount++;
            }
            else
            {
                var fieldVm = groupVm.Fields.FirstOrDefault(f => f.Name.Equals(edit.Name, StringComparison.OrdinalIgnoreCase));
                if (fieldVm is null)
                {
                    var newField = new MetadataFieldViewModel(edit.Group, edit.Name, edit.NewValue, true, originalValue: string.Empty);
                    groupVm.Fields.Add(newField);
                    populatedCount++;
                }
                else
                {
                    fieldVm.Value = edit.NewValue;
                    populatedCount++;
                }
            }
        }

        StatusMessage = $"Populated {populatedCount} realistic metadata fields! Click 'Save Edits' to write to file.";
    }

    [RelayCommand]
    private async Task ScrubCurrentFileAsync()
    {
        if (!File.Exists(SourcePath)) return;

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var magicBytes = ReadLeadingBytes(SourcePath, 16);
            var route = _router.ResolveScrubber(SourcePath, magicBytes);

            if (!route.IsSupported || route.Handler is null)
            {
                ErrorMessage = "Scrubbing is not supported for this file format.";
                return;
            }

            string outputPath;
            if (OverwriteOriginalFile)
            {
                outputPath = SourcePath;
            }
            else
            {
                var dir = Path.GetDirectoryName(SourcePath) ?? Directory.GetCurrentDirectory();
                var name = Path.GetFileNameWithoutExtension(SourcePath);
                var ext = Path.GetExtension(SourcePath);
                outputPath = Path.Combine(dir, $"{name}.cleaned{ext}");
            }

            var result = await route.Handler.ScrubAsync(SourcePath, outputPath, ScrubProfile.CreateStripAll());

            StatusMessage = $"Scrubbed successfully! Saved to: {Path.GetFileName(outputPath)}";
            await HandleDroppedPathsAsync([outputPath]);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Scrub failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveEditsAsync()
    {
        if (!File.Exists(SourcePath)) return;

        // Collect all non-empty fields in MetadataGroups or modified fields
        var modifiedEdits = MetadataGroups
            .SelectMany(g => g.Fields)
            .Where(f => f.IsModified || !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => new MetadataEdit(f.Group, f.Name, f.Value))
            .ToList();

        if (modifiedEdits.Count == 0)
        {
            StatusMessage = "No changes detected to save.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var magicBytes = ReadLeadingBytes(SourcePath, 16);
            var route = _router.ResolveWriter(SourcePath, magicBytes);

            if (!route.IsSupported || route.Handler is null)
            {
                ErrorMessage = "Editing is not supported for this file format.";
                return;
            }

            string outputPath;
            if (OverwriteOriginalFile)
            {
                outputPath = SourcePath;
            }
            else
            {
                var dir = Path.GetDirectoryName(SourcePath) ?? Directory.GetCurrentDirectory();
                var name = Path.GetFileNameWithoutExtension(SourcePath);
                var ext = Path.GetExtension(SourcePath);
                outputPath = Path.Combine(dir, $"{name}.edited{ext}");
            }

            var result = await route.Handler.WriteAsync(SourcePath, outputPath, modifiedEdits);

            StatusMessage = $"Saved {result.AppliedEdits} edit(s) successfully to: {outputPath}";
            await HandleDroppedPathsAsync([outputPath]);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Save edits failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildGroups(InspectionResult result)
    {
        MetadataGroups.Clear();

        foreach (var group in result.Document.GroupedFields
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var fields = group
                .Select(field => new MetadataFieldViewModel(group.Key, field.Name, field.Value, field.IsSensitive))
                .ToList();

            MetadataGroups.Add(new MetadataGroupViewModel(group.Key, fields));
        }
    }

    private static byte[] ReadLeadingBytes(string path, int count)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[count];
        var bytesRead = stream.Read(buffer, 0, count);
        return buffer.Take(bytesRead).ToArray();
    }
}
