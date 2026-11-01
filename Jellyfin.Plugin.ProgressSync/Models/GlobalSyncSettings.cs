using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Server-wide progress sync profiles.
/// </summary>
public sealed record GlobalSyncSettings
{
    /// <summary>Gets the configured global sync profiles.</summary>
    public IReadOnlyList<GlobalSyncProfile> Profiles { get; init; } = [];

    /// <summary>Gets a value indicating whether any profile is active.</summary>
    public bool IsActive => Profiles.Any(profile => profile.IsActive);

    /// <summary>
    /// Returns every profile's destinations for an item and source user.
    /// </summary>
    /// <param name="sourceUserId">The user whose progress changed.</param>
    /// <param name="item">The changed item.</param>
    /// <returns>Users to receive the progress update.</returns>
    public Guid[] TargetsOf(Guid sourceUserId, BaseItem item)
        => Profiles.SelectMany(profile => profile.TargetsOf(sourceUserId, item)).Distinct().ToArray();
}
