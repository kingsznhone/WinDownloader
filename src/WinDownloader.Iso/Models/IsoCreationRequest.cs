// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace WinDownloader.Iso.Models;

public sealed record IsoCreationRequest(
    string StagingDirectory,
    string OutputIsoPath,
    string VolumeLabel)
{
    /// <summary>
    /// 进度回调，携带结构化的 ISO 操作进度信息。
    /// </summary>
    public Action<IsoOperationProgress>? OnProgress { get; init; }
}
