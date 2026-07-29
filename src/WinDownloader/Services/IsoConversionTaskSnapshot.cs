using WinDownloader.Models;

namespace WinDownloader.Services;

public sealed record IsoConversionTaskSnapshot(string Sha256, EsdToIsoTaskSnapshot Snapshot);
