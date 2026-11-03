using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A user's maximum number of simultaneous playback sessions.
/// </summary>
/// <param name="UserId">The user identifier.</param>
/// <param name="MaxConcurrentStreams">The maximum number of concurrent streams.</param>
public sealed record ConcurrentStreamLimit(Guid UserId, int MaxConcurrentStreams);
