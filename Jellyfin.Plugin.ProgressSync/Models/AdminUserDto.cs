using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A user option shown on the admin settings page.
/// </summary>
/// <param name="Id">The user id.</param>
/// <param name="Name">The user name.</param>
public sealed record AdminUserDto(Guid Id, string Name);
