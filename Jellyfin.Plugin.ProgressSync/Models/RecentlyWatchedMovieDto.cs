using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A movie a user has watched.
/// </summary>
/// <param name="ItemId">The movie id.</param>
/// <param name="ItemName">The movie name.</param>
/// <param name="UserName">The user name.</param>
/// <param name="LastPlayedDate">When the movie was last played.</param>
public sealed record RecentlyWatchedMovieDto(
    Guid ItemId,
    string ItemName,
    string UserName,
    DateTime LastPlayedDate);
