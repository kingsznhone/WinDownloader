using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace POC;

/// <summary>
/// 原始 XML 行记录：完全平铺，每层用 Distinct/Where 过滤
/// </summary>
record RawFile(
    string LanguageCode,
    string Language,
    string Architecture,
    string EditionLoc,
    string Edition,
    string FileName,
    string FilePath,
    string Sha256,
    long Size,
    bool IsRetailOnly);

internal class Program
{
    private static readonly string _cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsImageDownloader", "poc_cache");

    private const string UpdateServiceUrl =
        "https://fe3.delivery.mp.microsoft.com/UpdateMetadataService/updates/search/v1/bydeviceinfo";

    private static readonly HttpClient _httpClient;

    static Program()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var handler = new HttpClientHandler();
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsImageDownloader-POC/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== WindowsImageDownloader POC: 交互式下载选择器 ===\n");

        var sw = Stopwatch.StartNew();
        Directory.CreateDirectory(_cacheDir);

        try
        {
            // 获取数据
            Console.WriteLine("📡 步骤 1: 调用 Windows Update REST API...");
            var (cabUrl, expectedDigest) = await SearchCatalogAsync();
            Console.WriteLine($"   ✅ 获取成功\n");

            Console.WriteLine("📥 步骤 2: 下载 products.cab...");
            var cabPath = Path.Combine(_cacheDir, "products.cab");
            await DownloadWithConditionalGetAsync(cabUrl, cabPath);

            Console.WriteLine("🔐 步骤 3: SHA-256 校验...");
            if (!await VerifySha256Async(cabPath, expectedDigest))
            {
                Console.WriteLine("   ❌ SHA-256 校验失败！");
                File.Delete(cabPath);
                return;
            }
            Console.WriteLine("   ✅ SHA-256 校验通过\n");

            Console.WriteLine("📦 步骤 4: 解压 products.cab → products.xml...");
            var xmlPath = Path.Combine(_cacheDir, "products.xml");
            ExtractProductsXml(cabPath, xmlPath);
            Console.WriteLine($"   ✅ 已解压到: {xmlPath}\n");

            // 步骤 5: 解析为平铺 List<RawFile>
            Console.WriteLine("🔍 步骤 5: 解析 products.xml...");
            var allFiles = ParseXml(xmlPath);
            Console.WriteLine($"   ✅ 共 {allFiles.Count} 条记录，"
                + $"{allFiles.Select(f => f.LanguageCode).Distinct().Count()} 种语言\n");

            sw.Stop();
            Console.WriteLine($"⏱️ 数据加载耗时: {sw.Elapsed.TotalSeconds:F1} 秒\n");

            // 步骤 6: 交互式选择（全部基于 Distinct + Where）
            Console.WriteLine("═══════════════════════════════════════");
            Console.WriteLine("       交互式下载选择器");
            Console.WriteLine("═══════════════════════════════════════\n");
            InteractiveSelect(allFiles);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ 错误: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    // ================================================================
    //  解析 XML → 平铺 List<RawFile>
    // ================================================================

    static List<RawFile> ParseXml(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        return doc.Descendants(ns + "File")
            .Select(f => new RawFile(
                LanguageCode: f.Element(ns + "LanguageCode")?.Value ?? "",
                Language: f.Element(ns + "Language")?.Value ?? "",
                Architecture: f.Element(ns + "Architecture")?.Value ?? "",
                EditionLoc: f.Element(ns + "Edition_Loc")?.Value ?? "",
                Edition: f.Element(ns + "Edition")?.Value ?? "",
                FileName: f.Element(ns + "FileName")?.Value ?? "",
                FilePath: f.Element(ns + "FilePath")?.Value ?? "",
                Sha256: f.Element(ns + "Sha256")?.Value ?? "",
                Size: long.TryParse(f.Element(ns + "Size")?.Value, out var s) ? s : 0,
                IsRetailOnly: f.Element(ns + "IsRetailOnly")?.Value == "True"
            ))
            .ToList();
    }

    // ================================================================
    //  交互式选择：Distinct + Where
    // ================================================================

    static void InteractiveSelect(List<RawFile> allFiles)
    {
        // --- 第1步：选语言 ---
        var languages = allFiles
            .Select(f => (f.LanguageCode, f.Language))
            .Distinct()
            .OrderBy(x => x.LanguageCode)
            .ToList();

        Console.WriteLine("📌 第1步：请选择语言");
        var lang = SelectItem(languages,
            x => $"  {x.LanguageCode,-12} {x.Language}",
            x => $"{x.LanguageCode} ({x.Language})");

        // 过滤
        var afterLang = allFiles.Where(f => f.LanguageCode == lang.LanguageCode).ToList();

        // --- 第2步：选架构 ---
        var architectures = afterLang
            .Select(f => f.Architecture)
            .Distinct()
            .Order()
            .ToList();

        Console.WriteLine($"\n📌 第2步：{lang.LanguageCode} - 请选择架构");
        var arch = SelectItem(architectures,
            a => $"  {a,-8}",
            a => a);

        // 过滤
        var afterArch = afterLang.Where(f => f.Architecture == arch).ToList();

        // --- 第3步：选 Edition_Loc 分组 ---
        var groups = afterArch
            .Select(f => f.EditionLoc)
            .Distinct()
            .Order()
            .ToList();

        Console.WriteLine($"\n📌 第3步：{lang.LanguageCode} {arch} - 请选择分组");
        var group = SelectItem(groups,
            g =>
            {
                var count = afterArch.Where(f => f.EditionLoc == g).Select(f => f.Edition).Distinct().Count();
                return $"  {g,-20} {DescribeGroup(g)}（{count} 个版本）";
            },
            g => g);

        // 过滤
        var afterGroup = afterArch.Where(f => f.EditionLoc == group).ToList();

        // --- 第4步：选具体 Edition ---
        var editions = afterGroup
            .Select(f => f.Edition)
            .Distinct()
            .Order()
            .ToList();

        Console.WriteLine($"\n📌 第4步：{lang.LanguageCode} {arch} {group} - 请选择版本");
        var editionName = SelectItem(editions,
            e =>
            {
                var first = afterGroup.First(f => f.Edition == e);
                return $"  {e,-35} {FormatSize(first.Size),10}  {first.FileName}";
            },
            e => e);

        // 定位最终记录（可能有 2 条（x64 和 ARM64 各 1 条），但前面已选好架构了所以只有 1 条）
        var result = afterGroup.First(f => f.Edition == editionName);

        // --- 第5步：显示结果 ---
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine("✅ 选择完成！下载信息如下：");
        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine($"  语言:       {result.LanguageCode} ({result.Language})");
        Console.WriteLine($"  架构:       {result.Architecture}");
        Console.WriteLine($"  分组:       {result.EditionLoc}");
        Console.WriteLine($"  版本:       {result.Edition}");
        Console.WriteLine($"  文件名:     {result.FileName}");
        Console.WriteLine($"  大小:       {FormatSize(result.Size)}");
        Console.WriteLine($"  零售版:     {(result.IsRetailOnly ? "是" : "否")}");
        Console.WriteLine($"  SHA-256:    {result.Sha256}");
        Console.WriteLine($"\n  下载地址:");
        Console.WriteLine($"  {result.FilePath}");
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine("💡 提示: 将此 URL 粘贴到浏览器或下载工具即可下载。");
    }

    /// <summary>
    /// 通用交互选择器
    /// </summary>
    static T SelectItem<T>(List<T> items, Func<T, string> display, Func<T, string> label)
    {
        while (true)
        {
            Console.WriteLine($"\n  可选项目 ({items.Count} 项):");
            for (int i = 0; i < items.Count; i++)
            {
                Console.WriteLine($"  [{i + 1,2}] {display(items[i])}");
            }

            Console.Write($"\n  请输入编号 (1-{items.Count})，或 q 退出: ");
            var input = Console.ReadLine()?.Trim();

            if (input?.ToLower() == "q")
            {
                Console.WriteLine("已退出。");
                Environment.Exit(0);
            }

            if (int.TryParse(input, out var index) && index >= 1 && index <= items.Count)
            {
                var selected = items[index - 1];
                Console.WriteLine($"  → 已选择: {label(selected)}");
                return selected;
            }

            Console.WriteLine("  ⚠️ 无效输入，请重新选择。");
        }
    }

    // ================================================================
    //  辅助方法
    // ================================================================

    static string DescribeGroup(string editionLoc) => editionLoc switch
    {
        "%CLIENT%" => "消费者零售版（家庭版/专业版/教育版系列）",
        "%ENTERPRISE%" => "企业批量许可版",
        "%ENTERPRISE_N%" => "企业批量许可版 N（无媒体播放器）",
        "%BASE_CHINA%" => "中国特供版",
        _ => editionLoc
    };

    static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    // ================================================================
    //  Windows Update API / 下载 / 校验 / 解压
    // ================================================================

    static async Task<(string url, string digest)> SearchCatalogAsync()
    {
        var requestBody = new
        {
            Products = "PN=Windows.Products.Cab.amd64&V=26100.0.0.0",
            DeviceAttributes = "DUScan=1;OSVersion=10.0.026100.1"
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(UpdateServiceUrl, jsonContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.GetArrayLength() == 0)
            throw new InvalidOperationException("Windows Update 服务未返回产品目录（返回空数组）");

        var firstResult = root[0];
        var fileLocations = firstResult.GetProperty("FileLocations");
        var url = fileLocations[0].GetProperty("Url").GetString()
            ?? throw new InvalidOperationException("URL 为空");
        var digest = fileLocations[0].GetProperty("Digest").GetString()
            ?? throw new InvalidOperationException("Digest 为空");

        return (url, digest);
    }

    static async Task DownloadWithConditionalGetAsync(string url, string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        long existingBytes = fileInfo.Exists ? fileInfo.Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (fileInfo.Exists)
            request.Headers.IfModifiedSince = fileInfo.LastWriteTimeUtc;

        if (existingBytes > 0)
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            Console.WriteLine($"   ⏭️ 文件未更新，跳过下载（{filePath}）");
            return;
        }

        response.EnsureSuccessStatusCode();

        var downloadPath = filePath + ".download";
        using var responseStream = await response.Content.ReadAsStreamAsync();
        using (var fileStream = new FileStream(
            downloadPath,
            existingBytes > 0 ? FileMode.Append : FileMode.Create))
        {
            await responseStream.CopyToAsync(fileStream);
        }

        File.Move(downloadPath, filePath, overwrite: true);

        if (response.Content.Headers.LastModified.HasValue)
            File.SetLastWriteTimeUtc(filePath, response.Content.Headers.LastModified.Value.UtcDateTime);
    }

    static async Task<bool> VerifySha256Async(string filePath, string expectedBase64)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream);
        var actualBase64 = Convert.ToBase64String(hashBytes);

        var match = string.Equals(expectedBase64, actualBase64, StringComparison.Ordinal);
        if (!match)
        {
            Console.WriteLine($"   预期: {expectedBase64}");
            Console.WriteLine($"   实际: {actualBase64}");
        }

        return match;
    }

    static void ExtractProductsXml(string cabPath, string outputPath)
    {
        var psi = new ProcessStartInfo("expand")
        {
            ArgumentList = { cabPath, "-F:products.xml", outputPath },
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 expand.exe");
        process.WaitForExit(TimeSpan.FromSeconds(30));

        if (process.ExitCode != 0)
        {
            var errors = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"expand.exe 返回错误 (ExitCode={process.ExitCode}): {errors}");
        }

        if (!File.Exists(outputPath))
            throw new FileNotFoundException("expand.exe 未生成 products.xml");
    }
}
