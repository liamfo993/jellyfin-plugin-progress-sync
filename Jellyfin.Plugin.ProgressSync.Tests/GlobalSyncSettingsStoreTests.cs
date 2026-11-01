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
            Profiles =
            [
                new GlobalSyncProfile
                {
                    Id = Guid.NewGuid(),
                    SourceUserId = Guid.NewGuid(),
                    TargetUserId = Guid.NewGuid(),
                    Mode = LinkMode.Follow,
                    Movies = true,
                    Series = true
                }
            ]
        };
        var store = new GlobalSyncSettingsStore(path);

        store.Save(settings);

        Assert.Equal(settings.Profiles[0], store.Current.Profiles[0]);
        Assert.Equal(settings.Profiles, new GlobalSyncSettingsStore(path).Current.Profiles);
    }

    [Fact]
    public void Load_MigratesLegacyTwoWaySettingsToProfile()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "global-settings.json");
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        File.WriteAllText(path, $$"""
            {
              "UserAId": "{{source}}",
              "UserBId": "{{target}}",
              "Movies": true,
              "Series": false,
              "Music": true
            }
            """);

        var settings = new GlobalSyncSettingsStore(path).Current;

        var profile = Assert.Single(settings.Profiles);
        Assert.True(settings.IsActive);
        Assert.Equal(source, profile.SourceUserId);
        Assert.Equal(target, profile.TargetUserId);
        Assert.Equal(LinkMode.Mirror, profile.Mode);
        Assert.True(profile.Movies);
        Assert.True(profile.Music);
    }
}