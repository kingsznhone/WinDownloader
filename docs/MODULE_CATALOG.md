# WindowsImageDownloader — 产品目录获取模块

## 概述

`UpdateCatalogService` 从 Microsoft Update Catalog 获取 Windows 安装映像产品目录，下载并校验 `products.cab`，解压 `products.xml`，解析为 `RawFile` 列表供 UI 筛选和入队下载。

## 文件清单

| 文件 | 说明 |
|------|------|
| `Interfaces/IUpdateCatalogService.cs` | 服务接口 |
| `Services/UpdateCatalogService.cs` | 服务实现 |
| `Models/RawFile.cs` | 产品目录条目模型 |
| `Models/RawFileGroup.cs` | UI 分组模型 |

## 接口

```csharp
public interface IUpdateCatalogService
{
    Task<IReadOnlyList<RawFile>> GetCatalogAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
```

## 流程

```text
GetCatalogAsync(forceRefresh)
  → SearchCatalogAsync
      POST /updates/search/v1/bydeviceinfo
      获取 products.cab URL 和 Digest
  → 判断缓存是否可用
  → DownloadCatalogCabAsync
      下载到 .download 临时文件后原子替换
  → VerifySha256Async
      校验 CAB 摘要
  → ExtractProductsXmlAsync
      调用 expand.exe 解压 products.xml
  → ParseProductsXml
      XDocument 解析为 IReadOnlyList<RawFile>
```

## RawFile 字段来源

| 字段 | 来源 |
|------|------|
| `LanguageCode` / `Language` | XML 语言字段 |
| `Architecture` | XML 架构字段 |
| `EditionLoc` / `Edition` | XML edition 字段 |
| `FileName` | ESD 文件名 |
| `FilePath` | ESD 下载 URL |
| `Sha256` | ESD SHA-256 |
| `Size` | ESD 文件大小 |
| `IsRetailOnly` | 零售限制标记 |

## 缓存

缓存目录：

```text
%LocalAppData%\WindowsImageDownloader\catalog_cache\
```

缓存内容：

- `products.cab`
- `products.xml`
- 下载中的 `.download` 临时文件

## 注意事项

- `expand.exe` 是 Windows 系统组件，缺失时无法解压 CAB。
- 当前 `Products` 和 `DeviceAttributes` 参数仍硬编码为 Windows 11 24H2 amd64，未来如要支持版本/架构选择，需要动态构造请求体。
- `forceRefresh = true` 会重新请求并刷新本地缓存。
