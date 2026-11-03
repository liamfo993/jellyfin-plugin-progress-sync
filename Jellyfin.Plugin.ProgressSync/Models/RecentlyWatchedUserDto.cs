using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A user listed in the watch history dashboard.
/// </summary>
/// <param name="Id">The user id.</param>
/// <param name="Name">The user name.</param>
public sealed record RecentlyWatchedUserDto(Guid Id, string Name);
