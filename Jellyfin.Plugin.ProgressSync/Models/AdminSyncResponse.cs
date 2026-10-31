namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Result of an admin copy or continuous sync setup.
/// </summary>
/// <param name="Copy">The initial copy result, if it completed.</param>
/// <param name="Link">The active continuous link, or null for one-time copies.</param>
/// <param name="CopyError">Why the initial copy failed, if the link remains active.</param>
public sealed record AdminSyncResponse(
    CopyResult? Copy,
    AdminLinkDto? Link,
    string? CopyError);
