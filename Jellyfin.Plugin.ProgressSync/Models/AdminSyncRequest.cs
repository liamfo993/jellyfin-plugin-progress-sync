using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// An admin request to copy progress once or create a continuous link.
/// </summary>
/// <param name="ItemId">The selected movie, series, or audio item.</param>
/// <param name="SourceUserId">The user whose progress is copied.</param>
/// <param name="TargetUserId">The user who receives progress.</param>
/// <param name="Mode">The continuous sync mode, or null for a one-time copy.</param>
public sealed record AdminSyncRequest(
    Guid ItemId,
    Guid SourceUserId,
    Guid TargetUserId,
    LinkMode? Mode);
