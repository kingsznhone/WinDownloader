using System.Windows.Input;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.ViewModels;

/// <summary>
/// Wraps a <see cref="RawFileGroup"/> with the download command for use in
/// <see cref="Views.Pages.SelectionPage"/>'s <c>ListView</c> item template.
/// </summary>
public sealed class RawFileItemViewModel(RawFileGroup group, ICommand downloadCommand)
{
    public RawFileGroup FileGroup { get; } = group;

    /// <summary>
    /// Invoked when the user clicks "下载 ESD".
    /// The command parameter is <see cref="RawFile"/> from <see cref="FileGroup"/>.
    /// </summary>
    public ICommand DownloadCommand { get; } = downloadCommand;
}
