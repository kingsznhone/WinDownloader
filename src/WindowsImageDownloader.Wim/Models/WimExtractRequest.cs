namespace WindowsImageDownloader.Wim;

public sealed record WimExtractRequest(
    string SourceImagePath,
    int ImageIndex,
    string DestinationDirectory);
