using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A user's maximum number of simultaneous playback sessions.
/// </summary>
/// <param name="UserId">The user identifier.</param>
/// <param name="MaxConcurrentStreams">The maximum number of concurrent streams.</param>
/// <param name="StopOldest">Whether the oldest stream is stopped instead of the newest.</param>
public sealed record ConcurrentStreamLimit(Guid UserId, int MaxConcurrentStreams, bool StopOldest = false);
