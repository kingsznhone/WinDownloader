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
            XDocument 解析为 RawFile 列表
```

目录搜索请求当前使用以下硬编码参数：

| 参数 | 值 |
|------|------|
| `Products` | `PN=Windows.Products.Cab.amd64&V=26300.0.0.0` |
| `DeviceAttributes` | `DUScan=1;OSVersion=10.0.026300.1` |

查询基线为 build 26300、amd64；UI 的架构筛选不改变请求参数。

## RawFile 字段来源

从 XML 的 `File` 元素读取语言、架构、版本、文件名、URL、SHA-256、大小及零售标记；字段含义见 [数据模型](MODULE_MODELS.md#rawfile)。

## 缓存

缓存目录：

```text
%LocalAppData%\WindowsImageDownloader\catalog_cache\
```

缓存包含 `products.cab`、`products.xml` 和下载中的 `.download` 临时文件。

## 注意事项

- `expand.exe` 是 Windows 系统组件，缺失时无法解压 CAB。
- `forceRefresh = true` 会重新请求并刷新本地缓存。
