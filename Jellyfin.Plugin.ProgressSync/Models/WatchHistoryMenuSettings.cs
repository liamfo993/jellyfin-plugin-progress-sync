namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Whether to show Watch History in Jellyfin's user menu.
/// </summary>
/// <param name="Enabled">Whether the user-menu link is enabled.</param>
public sealed record WatchHistoryMenuSettings(bool Enabled = false);
