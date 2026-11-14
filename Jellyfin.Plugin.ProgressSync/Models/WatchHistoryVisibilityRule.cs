using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// The users whose watch history a viewer may see.
/// </summary>
/// <param name="UserId">The viewer's user id.</param>
/// <param name="VisibleUserIds">The users whose history is visible to the viewer.</param>
public sealed record WatchHistoryVisibilityRule(Guid UserId, IReadOnlyList<Guid> VisibleUserIds);
