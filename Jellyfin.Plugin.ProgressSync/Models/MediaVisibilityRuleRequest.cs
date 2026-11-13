using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Admin request to hide an item from a user or all users.
/// </summary>
/// <param name="ItemId">The selected item or collection root.</param>
/// <param name="UserId">The target user, or <see langword="null"/> for every enabled user.</param>
/// <param name="VisibleFromMonth">Optional month the item becomes visible each year.</param>
/// <param name="VisibleFromDay">Optional day the item becomes visible each year.</param>
/// <param name="VisibleThroughMonth">Optional final visible month each year.</param>
/// <param name="VisibleThroughDay">Optional final visible day each year.</param>
public sealed record MediaVisibilityRuleRequest(
    Guid ItemId,
    Guid? UserId,
    int? VisibleFromMonth,
    int? VisibleFromDay,
    int? VisibleThroughMonth,
    int? VisibleThroughDay);
