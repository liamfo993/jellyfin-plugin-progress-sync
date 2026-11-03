namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Admin changes for Pushover notification settings.
/// </summary>
/// <param name="AppToken">Optional replacement application token.</param>
/// <param name="UserKey">Optional replacement user or group key.</param>
/// <param name="ClearCredentials">Whether to remove stored credentials.</param>
/// <param name="NotifyAdded">Whether to report library additions.</param>
/// <param name="NotifyWatched">Whether to report watched items.</param>
/// <param name="NotifyDeleted">Whether to report library deletions.</param>
/// <param name="NotifyPlaybackStarted">Whether to report playback starts.</param>
/// <param name="NotifyPlaybackStopped">Whether to report playback stops.</param>
public sealed record PushoverNotificationsUpdate(
    string? AppToken,
    string? UserKey,
    bool ClearCredentials,
    bool NotifyAdded,
    bool NotifyWatched,
    bool NotifyDeleted,
    bool NotifyPlaybackStarted,
    bool NotifyPlaybackStopped);
