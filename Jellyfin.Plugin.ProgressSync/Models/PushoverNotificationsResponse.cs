namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Pushover settings returned to the admin UI without exposing saved credentials.
/// </summary>
/// <param name="IsConfigured">Whether both credentials are stored.</param>
/// <param name="NotifyAdded">Whether library additions are reported.</param>
/// <param name="NotifyWatched">Whether watched items are reported.</param>
/// <param name="NotifyDeleted">Whether library deletions are reported.</param>
/// <param name="NotifyPlaybackStarted">Whether playback starts are reported.</param>
/// <param name="NotifyPlaybackStopped">Whether playback stops are reported.</param>
public sealed record PushoverNotificationsResponse(
    bool IsConfigured,
    bool NotifyAdded,
    bool NotifyWatched,
    bool NotifyDeleted,
    bool NotifyPlaybackStarted,
    bool NotifyPlaybackStopped);
