using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Settings and user options for the admin settings page.
/// </summary>
/// <param name="Settings">Current global sync settings.</param>
/// <param name="Users">Enabled users available for selection.</param>
public sealed record GlobalSyncSettingsResponse(
    GlobalSyncSettings Settings,
    IReadOnlyList<AdminUserDto> Users);
