using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Metdatwip.App.ViewModels;

public partial class MetadataGroupViewModel : ObservableObject
{
    public MetadataGroupViewModel(string groupName, IEnumerable<MetadataFieldViewModel> fields)
    {
        GroupName = groupName;
        Fields = new ObservableCollection<MetadataFieldViewModel>(fields);
    }

    public string GroupName { get; }

    public ObservableCollection<MetadataFieldViewModel> Fields { get; }

    public int SensitiveCount => Fields.Count(field => field.IsSensitive);

    public bool HasSensitiveFields => SensitiveCount > 0;

    public string Header => $"{GroupName} ({Fields.Count})";
}
