using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A movie or episode a user has watched.
/// </summary>
/// <param name="ItemId">The item id.</param>
/// <param name="ItemName">The item name.</param>
/// <param name="ItemType">The item type.</param>
/// <param name="LastPlayedDate">When the item was last played.</param>
public sealed record RecentlyWatchedItemDto(
    Guid ItemId,
    string ItemName,
    string ItemType,
    DateTime LastPlayedDate);
