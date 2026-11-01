using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A global sync configuration for one pair of users.
/// </summary>
public sealed record GlobalSyncProfile
{
    /// <summary>Gets the profile identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the user whose progress is the source for one-way sync.</summary>
    public Guid SourceUserId { get; init; }

    /// <summary>Gets the user who receives progress in one-way sync.</summary>
    public Guid TargetUserId { get; init; }

    /// <summary>Gets how progress flows between the users.</summary>
    public LinkMode Mode { get; init; } = LinkMode.Follow;

    /// <summary>Gets a value indicating whether movies are synchronized.</summary>
    public bool Movies { get; init; }

    /// <summary>Gets a value indicating whether series are synchronized.</summary>
    public bool Series { get; init; }

    /// <summary>Gets a value indicating whether music tracks are synchronized.</summary>
    public bool Music { get; init; }

    /// <summary>Gets when the profile was created.</summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>Gets a value indicating whether the profile is valid and enabled.</summary>
    public bool IsActive => Id != Guid.Empty
        && SourceUserId != Guid.Empty
        && TargetUserId != Guid.Empty
        && SourceUserId != TargetUserId
        && Enum.IsDefined(Mode)
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
    /// Gets the recipient for a matching source user and item.
    /// </summary>
    /// <param name="sourceUserId">The user whose progress changed.</param>
    /// <param name="item">The changed item.</param>
    /// <returns>The users that should receive this progress update.</returns>
    public Guid[] TargetsOf(Guid sourceUserId, BaseItem item)
    {
        if (!IsActive || !Includes(item))
        {
            return [];
        }

        if (sourceUserId == SourceUserId)
        {
            return [TargetUserId];
        }

        return Mode == LinkMode.Mirror && sourceUserId == TargetUserId ? [SourceUserId] : [];
    }
}
