namespace WinDownloader.Models;

/// <summary>
/// Semantic color variant for <see cref="Views.Controls.TagControl"/>, inspired by Element Plus el-tag.
/// </summary>
public enum TagType
{
    /// <summary>Default neutral tag (gray).</summary>
    Default,

    /// <summary>Primary accent tag (blue).</summary>
    Primary,

    /// <summary>Success / positive tag (green).</summary>
    Success,

    /// <summary>Warning tag (orange).</summary>
    Warning,

    /// <summary>Danger / error tag (red).</summary>
    Danger,

    /// <summary>Informational tag (light blue).</summary>
    Info,
}
