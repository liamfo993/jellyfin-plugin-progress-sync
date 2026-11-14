using System;
using System.IO;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class WatchHistoryVisibilityStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ps-watch-visibility-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "watch-history-visibility.json");

    [Fact]
    public void DefaultsToEveryoneAndPersistsConfiguredRules()
    {
        var store = NewStore();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        Assert.True(store.CanView(alice, bob));
        store.Save(new WatchHistoryVisibilitySettings(true, [
            new WatchHistoryVisibilityRule(alice, [alice]),
            new WatchHistoryVisibilityRule(bob, [bob, alice])
        ]));

        var reloaded = NewStore();
        Assert.True(reloaded.CanView(alice, alice));
        Assert.False(reloaded.CanView(alice, bob));
        Assert.True(reloaded.CanView(bob, alice));
        Assert.True(reloaded.CanView(bob, bob));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private WatchHistoryVisibilityStore NewStore()
    {
        return new WatchHistoryVisibilityStore(SettingsPath, NullLogger<WatchHistoryVisibilityStore>.Instance);
    }
}
