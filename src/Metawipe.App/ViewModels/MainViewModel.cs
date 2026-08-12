using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Metawipe.App.Services;

namespace Metawipe.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MetadataInspectionService _inspectionService = new();

    [ObservableProperty]
    private string statusMessage = "Drop a file or folder to inspect metadata.";

    [ObservableProperty]
    private string sourcePath = "No file selected.";

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<MetadataGroupViewModel> MetadataGroups { get; } = [];

    public async Task HandleDroppedPathsAsync(IEnumerable<string> droppedPaths)
    {
        ArgumentNullException.ThrowIfNull(droppedPaths);

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _inspectionService.InspectAsync(droppedPaths);

            SourcePath = result.SourcePath;
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
            StatusMessage = "Could not inspect dropped items.";
            ErrorMessage = ex.Message;
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
                .Select(field => new MetadataFieldViewModel(field.Name, field.Value, field.IsSensitive))
                .ToList();

            MetadataGroups.Add(new MetadataGroupViewModel(group.Key, fields));
        }
    }
}
