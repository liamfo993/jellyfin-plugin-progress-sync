namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Persisted concurrent-stream settings for one user.
/// </summary>
/// <param name="MaxConcurrentStreams">The maximum number of concurrent streams.</param>
/// <param name="StopOldest">Whether the oldest stream is stopped instead of the newest.</param>
public sealed record ConcurrentStreamLimitSetting(int MaxConcurrentStreams, bool StopOldest);
