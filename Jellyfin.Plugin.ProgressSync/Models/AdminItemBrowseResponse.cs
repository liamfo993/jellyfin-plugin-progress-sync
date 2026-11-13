using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A page of media items available for admin visibility rules.
/// </summary>
/// <param name="Items">The media items in this page.</param>
/// <param name="HasMore">Whether another page of matching media items is available.</param>
public sealed record AdminItemBrowseResponse(
    IReadOnlyList<AdminItemDto> Items,
    bool HasMore);
