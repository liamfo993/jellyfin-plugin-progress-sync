using System.Collections.Generic;

namespace Jellyfin.Plugin.ProgressSync.Models;

/// <summary>
/// User options and configured concurrent-stream limits.
/// </summary>
/// <param name="Users">Enabled users that can be configured.</param>
/// <param name="Limits">Configured per-user limits.</param>
public sealed record ConcurrentStreamLimitsResponse(
    IReadOnlyList<AdminUserDto> Users,
    IReadOnlyList<ConcurrentStreamLimit> Limits);
