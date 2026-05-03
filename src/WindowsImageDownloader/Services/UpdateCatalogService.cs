using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Services;

public sealed class UpdateCatalogService : IUpdateCatalogService
{
    private const string UpdateServiceUrl =
        "https://fe3.delivery.mp.microsoft.com/UpdateMetadataService/updates/search/v1/bydeviceinfo";

    private readonly HttpClient httpClient;
    private readonly string cacheDirectory;

    public UpdateCatalogService()
    {
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsImageDownloader/0.1");

        cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsImageDownloader",
            "catalog_cache");
    }

    public async Task<IReadOnlyList<RawFile>> GetCatalogAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);

        var cabPath = Path.Combine(cacheDirectory, "products.cab");
        var xmlPath = Path.Combine(cacheDirectory, "products.xml");

        try
        {
            var (cabUrl, expectedDigest) = await SearchCatalogAsync(cancellationToken);

            if (forceRefresh || !File.Exists(cabPath) ||
                !await VerifySha256Async(cabPath, expectedDigest, cancellationToken))
            {
                await DownloadCatalogCabAsync(cabUrl, cabPath, cancellationToken);

                if (!await VerifySha256Async(cabPath, expectedDigest, cancellationToken))
                {
                    throw new InvalidOperationException("products.cab SHA-256 校验失败。");
                }
            }

            await ExtractProductsXmlAsync(cabPath, xmlPath, cancellationToken);
            return ParseProductsXml(xmlPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && File.Exists(xmlPath))
        {
            return ParseProductsXml(xmlPath);
        }
    }

    private async Task<(string Url, string Digest)> SearchCatalogAsync(CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            Products = "PN=Windows.Products.Cab.amd64&V=26100.0.0.0",
            DeviceAttributes = "DUScan=1;OSVersion=10.0.026100.1"
        };

        using var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.PostAsync(UpdateServiceUrl, jsonContent, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Windows Update 服务未返回产品目录。");
        }

        foreach (var result in root.EnumerateArray())
        {
            if (!result.TryGetProperty("FileLocations", out var fileLocations) ||
                fileLocations.ValueKind != JsonValueKind.Array ||
                fileLocations.GetArrayLength() == 0)
            {
                continue;
            }

            var location = fileLocations[0];
            var url = location.GetProperty("Url").GetString();
            var digest = location.GetProperty("Digest").GetString();

            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(digest))
            {
                return (url, digest);
            }
        }

        throw new InvalidOperationException("Windows Update 响应中没有可用的 products.cab 下载地址。");
    }

    private async Task DownloadCatalogCabAsync(
        string url,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var tempPath = outputPath + ".download";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static async Task<bool> VerifySha256Async(
        string filePath,
        string expectedBase64,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualBase64 = Convert.ToBase64String(hashBytes);
        return string.Equals(expectedBase64, actualBase64, StringComparison.Ordinal);
    }

    private static async Task ExtractProductsXmlAsync(
        string cabPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("无法确定 products.xml 输出目录。");

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        var processStartInfo = new ProcessStartInfo("expand.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        processStartInfo.ArgumentList.Add(cabPath);
        processStartInfo.ArgumentList.Add("-F:products.xml");
        processStartInfo.ArgumentList.Add(outputDirectory);

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("无法启动 expand.exe。");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var output = await outputTask;
            var error = await errorTask;
            throw new InvalidOperationException($"expand.exe 解压失败。{output}{error}");
        }

        if (!File.Exists(outputPath))
        {
            throw new FileNotFoundException("expand.exe 未生成 products.xml。", outputPath);
        }
    }

    private static IReadOnlyList<RawFile> ParseProductsXml(string xmlPath)
    {
        var document = XDocument.Load(xmlPath);
        var ns = document.Root?.GetDefaultNamespace() ?? XNamespace.None;

        return document.Descendants(ns + "File")
            .Select(file => new RawFile(
                LanguageCode: file.Element(ns + "LanguageCode")?.Value.Trim() ?? string.Empty,
                Language: file.Element(ns + "Language")?.Value.Trim() ?? string.Empty,
                Architecture: file.Element(ns + "Architecture")?.Value.Trim() ?? string.Empty,
                EditionLoc: file.Element(ns + "Edition_Loc")?.Value.Trim() ?? string.Empty,
                Edition: file.Element(ns + "Edition")?.Value.Trim() ?? string.Empty,
                FileName: file.Element(ns + "FileName")?.Value.Trim() ?? string.Empty,
                FilePath: file.Element(ns + "FilePath")?.Value.Trim() ?? string.Empty,
                Sha256: file.Element(ns + "Sha256")?.Value.Trim() ?? string.Empty,
                Size: long.TryParse(
                    file.Element(ns + "Size")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var size)
                        ? size
                        : 0,
                IsRetailOnly: string.Equals(
                    file.Element(ns + "IsRetailOnly")?.Value,
                    "True",
                    StringComparison.OrdinalIgnoreCase)))
            .Where(file => !string.IsNullOrWhiteSpace(file.FileName) &&
                !string.IsNullOrWhiteSpace(file.FilePath))
            .OrderBy(file => file.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Architecture, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.EditionGroupLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Edition, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
