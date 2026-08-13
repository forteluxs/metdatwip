using CommunityToolkit.Mvvm.ComponentModel;

namespace Metdatwip.App.ViewModels;

public partial class MetadataFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string value;

    public MetadataFieldViewModel(string group, string name, string value, bool isSensitive, string? originalValue = null)
    {
        Group = group;
        Name = name;
        this.value = value;
        OriginalValue = originalValue ?? value;
        IsSensitive = isSensitive;
    }

    public string Group { get; }

    public string Name { get; }

    public string OriginalValue { get; }

    public bool IsSensitive { get; }

    public bool IsModified => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);
}
