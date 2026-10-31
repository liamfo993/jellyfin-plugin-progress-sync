using System;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// A library item available for admin sync setup.
/// </summary>
/// <param name="Id">The item id.</param>
/// <param name="Name">The item name.</param>
/// <param name="Type">The Jellyfin item type.</param>
public sealed record AdminItemDto(Guid Id, string Name, string Type);
