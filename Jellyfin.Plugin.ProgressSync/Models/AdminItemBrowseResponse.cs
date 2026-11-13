using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A page of media items available for admin visibility rules.
/// </summary>
/// <param name="Items">The media items in this page.</param>
/// <param name="TotalCount">The total number of matching media items.</param>
public sealed record AdminItemBrowseResponse(
    IReadOnlyList<AdminItemDto> Items,
    int TotalCount);
