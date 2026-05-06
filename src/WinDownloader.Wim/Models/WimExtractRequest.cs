namespace WinDownloader.Wim;

public sealed record WimExtractRequest(
    string SourceImagePath,
    int ImageIndex,
    string DestinationDirectory);
