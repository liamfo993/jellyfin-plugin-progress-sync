using System;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ProgressSync;

/// <summary>
/// Registers the plugin's services with the server's dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<LinkStore>();
        serviceCollection.AddSingleton<GlobalSyncSettingsStore>();
        serviceCollection.AddSingleton<ConcurrentStreamLimitStore>();
        serviceCollection.AddSingleton<PushoverNotificationStore>();
        serviceCollection.AddSingleton<ProgressCopier>();
        serviceCollection.AddSingleton<WebInjectionStatus>();
        serviceCollection.AddHttpClient("ProgressSyncPushover", client => client.Timeout = TimeSpan.FromSeconds(10));
        serviceCollection.AddHostedService<LinkSyncService>();
        serviceCollection.AddHostedService<ConcurrentStreamLimitService>();
        serviceCollection.AddSingleton<PushoverNotificationService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<PushoverNotificationService>());
        serviceCollection.AddHostedService<WebInjectionService>();
    }
}
