using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A user's watch status for an item.
/// </summary>
/// <param name="UserId">The user id.</param>
/// <param name="UserName">The user name.</param>
/// <param name="CanAccess">Whether the user can access this item.</param>
/// <param name="Played">Whether the user has marked this item as played.</param>
/// <param name="InProgress">Whether the user has a resume position.</param>
/// <param name="ProgressPercent">The rounded progress percentage, when runtime is known.</param>
public sealed record UserWatchStatusDto(
    Guid UserId,
    string UserName,
    bool CanAccess,
    bool Played,
    bool InProgress,
    int? ProgressPercent);
