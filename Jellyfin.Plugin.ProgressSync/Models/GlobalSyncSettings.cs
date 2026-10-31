using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Server-wide two-way progress sync settings for one pair of users.
/// </summary>
public sealed record GlobalSyncSettings
{
    /// <summary>Gets the first user in the pair.</summary>
    public Guid UserAId { get; init; }

    /// <summary>Gets the second user in the pair.</summary>
    public Guid UserBId { get; init; }

    /// <summary>Gets a value indicating whether movies are synchronized.</summary>
    public bool Movies { get; init; }

    /// <summary>Gets a value indicating whether series are synchronized.</summary>
    public bool Series { get; init; }

    /// <summary>Gets a value indicating whether music tracks are synchronized.</summary>
    public bool Music { get; init; }

    /// <summary>Gets a value indicating whether this pair has at least one active scope.</summary>
    public bool IsActive => UserAId != Guid.Empty
        && UserBId != Guid.Empty
        && UserAId != UserBId
        && (Movies || Series || Music);

    /// <summary>
    /// Returns whether an item belongs to an enabled global scope.
    /// </summary>
    /// <param name="item">The changed item.</param>
    /// <returns><c>true</c> if the item is in a selected scope.</returns>
    public bool Includes(BaseItem item) => item switch
    {
        Movie => Movies,
        Episode => Series,
        Audio => Music,
        _ => false
    };

    /// <summary>
    /// Gets the other configured user when the item and source match this global rule.
    /// </summary>
    /// <param name="sourceUserId">The user whose progress changed.</param>
    /// <param name="item">The changed item.</param>
    /// <returns>The other user, or an empty array when the rule does not apply.</returns>
    public Guid[] TargetsOf(Guid sourceUserId, BaseItem item)
    {
        if (!IsActive || !Includes(item))
        {
            return [];
        }

        if (sourceUserId == UserAId)
        {
            return [UserBId];
        }

        return sourceUserId == UserBId ? [UserAId] : [];
    }
}
