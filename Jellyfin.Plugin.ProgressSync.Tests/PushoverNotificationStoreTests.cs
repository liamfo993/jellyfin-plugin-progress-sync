using System;
using System.IO;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class PushoverNotificationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-pushover-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void Save_PersistsSettingsAndPreservesCredentialsWhenFieldsAreBlank()
    {
        var path = Path.Combine(_dir, "pushover-notifications.json");
        var store = NewStore(path);
        store.Save(new PushoverNotificationsUpdate(
            " app-token ",
            " user-key ",
            false,
            true,
            false,
            true,
            false,
            true));

        var reloaded = NewStore(path);
        Assert.Equal("app-token", reloaded.Current.AppToken);
        Assert.Equal("user-key", reloaded.Current.UserKey);
        Assert.True(reloaded.Current.NotifyAdded);
        Assert.True(reloaded.Current.NotifyDeleted);
        Assert.True(reloaded.Current.NotifyPlaybackStopped);
        Assert.True(reloaded.Current.IsConfigured);

        reloaded.Save(new PushoverNotificationsUpdate(
            string.Empty,
            null,
            false,
            false,
            true,
            false,
            true,
            false));

        Assert.Equal("app-token", reloaded.Current.AppToken);
        Assert.Equal("user-key", reloaded.Current.UserKey);
        Assert.False(reloaded.Current.NotifyAdded);
        Assert.True(reloaded.Current.NotifyWatched);
        Assert.True(NewStore(path).Current.NotifyWatched);
    }

    [Fact]
    public void Save_CanClearCredentialsWithoutDisablingEventPreferences()
    {
        var store = NewStore(Path.Combine(_dir, "pushover-notifications.json"));
        store.Save(new PushoverNotificationsUpdate(
            "app-token",
            "user-key",
            false,
            true,
            false,
            false,
            false,
            false));

        store.Save(new PushoverNotificationsUpdate(
            null,
            null,
            true,
            true,
            false,
            false,
            false,
            false));

        Assert.False(store.Current.IsConfigured);
        Assert.True(store.Current.NotifyAdded);
    }

    private static PushoverNotificationStore NewStore(string path)
    {
        return new PushoverNotificationStore(path, NullLogger<PushoverNotificationStore>.Instance);
    }
}
