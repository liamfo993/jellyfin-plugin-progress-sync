using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A rule that hides an item from one user or every user, optionally outside an annual visible window.
/// </summary>
public sealed record MediaVisibilityRule(
    Guid Id,
    Guid ItemId,
    Guid? UserId,
    int? VisibleFromMonth,
    int? VisibleFromDay,
    int? VisibleThroughMonth,
    int? VisibleThroughDay,
    DateTime CreatedUtc)
{
    /// <summary>Gets the tag used to apply this rule through Jellyfin's parental visibility checks.</summary>
    public string VisibilityTag => "ProgressSync.Hidden." + Id.ToString("N");

    /// <summary>Determines whether this rule hides content on the supplied server-local date.</summary>
    /// <param name="date">The current server-local date.</param>
    /// <returns><see langword="true"/> when the item should be hidden.</returns>
    public bool IsHiddenOn(DateOnly date)
    {
        if (VisibleFromMonth is not { } fromMonth
            || VisibleFromDay is not { } fromDay
            || VisibleThroughMonth is not { } throughMonth
            || VisibleThroughDay is not { } throughDay)
        {
            return true;
        }

        var current = (date.Month * 100) + date.Day;
        var from = (fromMonth * 100) + fromDay;
        var through = (throughMonth * 100) + throughDay;
        var isVisible = from <= through
            ? current >= from && current <= through
            : current >= from || current <= through;
        return !isVisible;
    }
}
