namespace WinDownloader.Wim.Models;

public sealed record WimExtractRequest(
    string SourceImagePath,
    int ImageIndex,
    string DestinationDirectory);
