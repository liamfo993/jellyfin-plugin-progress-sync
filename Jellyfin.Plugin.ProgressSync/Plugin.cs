using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ProgressSync.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ProgressSync;

/// <summary>
/// Progress Sync plugin entry point.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The plugin id. Must match <c>guid</c> in build.yaml.
    /// </summary>
    public static readonly Guid PluginId = new("c16569f5-02ed-4d6a-bbdb-649455e512d5");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => "Progress Sync";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <inheritdoc />
    public override string Description => "Copy or keep watch progress in sync between users.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "WatchHistory",
                DisplayName = "Watch History",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.WatchHistory.html",
                EnableInMainMenu = true,
                MenuIcon = "history"
            },
            new PluginPageInfo
            {
                Name = "WatchHistory.js",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.WatchHistory.js"
            },
            new PluginPageInfo
            {
                Name = "PlaybackLimits",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.PlaybackLimits.html"
            },
            new PluginPageInfo
            {
                Name = "PlaybackLimits.js",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.PlaybackLimits.js"
            },
            new PluginPageInfo
            {
                Name = "Notifications",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.Notifications.html"
            },
            new PluginPageInfo
            {
                Name = "Notifications.js",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.Notifications.js"
            },
            new PluginPageInfo
            {
                Name = "MediaVisibility",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.MediaVisibility.html"
            },
            new PluginPageInfo
            {
                Name = "MediaVisibility.js",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.MediaVisibility.js"
            },
            new PluginPageInfo
            {
                Name = "ProgressSync.js",
                EmbeddedResourcePath = GetType().Namespace + ".Pages.ProgressSync.js"
            },
            new PluginPageInfo
            {
                Name = "ProgressSync",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        ];
    }
}
