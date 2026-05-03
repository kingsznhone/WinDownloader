namespace WindowsImageDownloader.Models;

public sealed record CatalogOption(string Value, string Label)
{
    public bool IsAll => string.IsNullOrEmpty(Value);

    public static CatalogOption All(string label) => new(string.Empty, label);

    public override string ToString() => Label;
}
