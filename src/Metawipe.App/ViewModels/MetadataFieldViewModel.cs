namespace Metawipe.App.ViewModels;

public sealed class MetadataFieldViewModel
{
    public MetadataFieldViewModel(string name, string value, bool isSensitive)
    {
        Name = name;
        Value = value;
        IsSensitive = isSensitive;
    }

    public string Name { get; }

    public string Value { get; }

    public bool IsSensitive { get; }
}
