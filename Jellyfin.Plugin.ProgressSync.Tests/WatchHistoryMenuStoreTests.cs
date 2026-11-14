using System;
using System.IO;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class WatchHistoryMenuStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ps-watch-history-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "watch-history-menu.json");

    [Fact]
    public void Save_PersistsSettingAndReloadsIt()
    {
        var store = NewStore();
        Assert.False(store.Current.Enabled);

        store.Save(new WatchHistoryMenuSettings(true));

        Assert.True(NewStore().Current.Enabled);
    }

    [Fact]
    public void SettingsCanBeUpdatedIndependentlyAndPersist()
    {
        var store = NewStore();
        Assert.False(store.Current.RatingsEnabled);
        store.SetRatingsEnabled(true);
        store.SetMenuEnabled(true);
        store.SetMenuEnabled(false);

        var reloaded = NewStore().Current;
        Assert.False(reloaded.Enabled);
        Assert.True(reloaded.RatingsEnabled);
        store.SetRatingsEnabled(false);
        Assert.False(NewStore().Current.RatingsEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private WatchHistoryMenuStore NewStore()
    {
        return new WatchHistoryMenuStore(SettingsPath, NullLogger<WatchHistoryMenuStore>.Instance);
    }
}
