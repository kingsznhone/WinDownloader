namespace POC.Models;

public sealed record WimExtractRequest(
    string SourceImagePath,
    int ImageIndex,
    string DestinationDirectory);
