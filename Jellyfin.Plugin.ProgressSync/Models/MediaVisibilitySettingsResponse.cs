using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// Users and visibility rules shown on the admin page.
/// </summary>
/// <param name="Users">Enabled Jellyfin users.</param>
/// <param name="Rules">Configured rules.</param>
public sealed record MediaVisibilitySettingsResponse(
    IReadOnlyList<AdminUserDto> Users,
    IReadOnlyList<MediaVisibilityRuleDto> Rules);
