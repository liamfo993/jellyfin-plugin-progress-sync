using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ProgressSync.Configuration;

/// <summary>
/// Jellyfin's base configuration type. Progress Sync's admin settings are managed by its
/// configuration page and persisted separately.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
}
