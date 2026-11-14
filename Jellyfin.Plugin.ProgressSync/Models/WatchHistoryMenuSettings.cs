namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Watch History display settings.
/// </summary>
/// <param name="Enabled">Whether the user-menu link is enabled.</param>
/// <param name="RatingsEnabled">Whether to show Ratings plugin averages on history covers.</param>
public sealed record WatchHistoryMenuSettings(bool Enabled = false, bool RatingsEnabled = false);
