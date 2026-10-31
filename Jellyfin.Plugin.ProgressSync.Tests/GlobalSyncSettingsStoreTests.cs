using System;
using System.IO;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class GlobalSyncSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-settings-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void Save_PublishesSettingsAndReloadsThem()
    {
        var path = Path.Combine(_dir, "global-settings.json");
        var settings = new GlobalSyncSettings
        {
            UserAId = Guid.NewGuid(),
            UserBId = Guid.NewGuid(),
            Movies = true,
            Series = true
        };
        var store = new GlobalSyncSettingsStore(path);

        store.Save(settings);

        Assert.Equal(settings, store.Current);
        Assert.Equal(settings, new GlobalSyncSettingsStore(path).Current);
    }
}