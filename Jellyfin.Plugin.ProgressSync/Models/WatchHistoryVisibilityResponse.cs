using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Watch history visibility settings and enabled users for the administrator UI.
/// </summary>
/// <param name="IsConfigured">Whether an administrator has configured visibility rules.</param>
/// <param name="Users">The enabled server users.</param>
/// <param name="Rules">The visible-user choices for each viewer.</param>
public sealed record WatchHistoryVisibilityResponse(
    bool IsConfigured,
    IReadOnlyList<AdminUserDto> Users,
    IReadOnlyList<WatchHistoryVisibilityRule> Rules);
