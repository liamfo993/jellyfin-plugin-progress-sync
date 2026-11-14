using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Configured per-user watch history visibility.
/// </summary>
/// <param name="IsConfigured">Whether an administrator has configured visibility rules.</param>
/// <param name="Rules">The visible-user choices for each viewer.</param>
public sealed record WatchHistoryVisibilitySettings(
    bool IsConfigured = false,
    IReadOnlyList<WatchHistoryVisibilityRule>? Rules = null);
