namespace Metawipe.App.ViewModels;

public sealed class MetadataGroupViewModel
{
    public MetadataGroupViewModel(string groupName, IReadOnlyList<MetadataFieldViewModel> fields)
    {
        GroupName = groupName;
        Fields = fields;
    }

    public string GroupName { get; }

    public IReadOnlyList<MetadataFieldViewModel> Fields { get; }

    public int SensitiveCount => Fields.Count(field => field.IsSensitive);

    public bool HasSensitiveFields => SensitiveCount > 0;

    public string Header => $"{GroupName} ({Fields.Count})";
}
