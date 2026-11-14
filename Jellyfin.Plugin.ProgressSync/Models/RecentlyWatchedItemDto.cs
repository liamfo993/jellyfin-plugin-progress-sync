using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A movie or episode in a user's watch history.
/// </summary>
/// <param name="ItemId">The item id.</param>
/// <param name="ItemName">The item name.</param>
/// <param name="ItemType">The item type.</param>
/// <param name="LastPlayedDate">When the item was last played.</param>
/// <param name="SeriesId">The visible series id, for episode artwork.</param>
/// <param name="ProgressPercent">The rounded playback percentage, for in-progress items.</param>
/// <param name="InProgress">Whether the item is partially watched.</param>
/// <param name="CurrentlyWatching">Whether the item is in an active playback session.</param>
public sealed record RecentlyWatchedItemDto(
    Guid ItemId,
    string ItemName,
    string ItemType,
    DateTime LastPlayedDate,
    Guid? SeriesId = null,
    int? ProgressPercent = null,
    bool InProgress = false,
    bool CurrentlyWatching = false);
