namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Pushover credentials and event selections.
/// </summary>
public sealed record PushoverNotificationSettings
{
    /// <summary>Gets the Pushover application token.</summary>
    public string? AppToken { get; init; }

    /// <summary>Gets the Pushover user or group key.</summary>
    public string? UserKey { get; init; }

    /// <summary>Gets a value indicating whether library additions are reported.</summary>
    public bool NotifyAdded { get; init; }

    /// <summary>Gets a value indicating whether watched items are reported.</summary>
    public bool NotifyWatched { get; init; }

    /// <summary>Gets a value indicating whether library deletions are reported.</summary>
    public bool NotifyDeleted { get; init; }

    /// <summary>Gets a value indicating whether playback starts are reported.</summary>
    public bool NotifyPlaybackStarted { get; init; }

    /// <summary>Gets a value indicating whether playback stops are reported.</summary>
    public bool NotifyPlaybackStopped { get; init; }

    /// <summary>Gets a value indicating whether both Pushover credentials are configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppToken) && !string.IsNullOrWhiteSpace(UserKey);
}
