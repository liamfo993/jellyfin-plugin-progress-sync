using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A visibility rule with the current item and user names for the admin page.
/// </summary>
/// <param name="Id">The rule id.</param>
/// <param name="ItemId">The item id.</param>
/// <param name="ItemName">The item name.</param>
/// <param name="ItemType">The Jellyfin item type.</param>
/// <param name="UserId">The target user id, or <see langword="null"/> for all users.</param>
/// <param name="UserName">The target user name, or <see langword="null"/> for all users.</param>
/// <param name="VisibleFromMonth">The annual window's first visible month, if scheduled.</param>
/// <param name="VisibleFromDay">The annual window's first visible day, if scheduled.</param>
/// <param name="VisibleThroughMonth">The annual window's final visible month, if scheduled.</param>
/// <param name="VisibleThroughDay">The annual window's final visible day, if scheduled.</param>
/// <param name="CreatedUtc">When the rule was created.</param>
public sealed record MediaVisibilityRuleDto(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string ItemType,
    Guid? UserId,
    string? UserName,
    int? VisibleFromMonth,
    int? VisibleFromDay,
    int? VisibleThroughMonth,
    int? VisibleThroughDay,
    DateTime CreatedUtc);
