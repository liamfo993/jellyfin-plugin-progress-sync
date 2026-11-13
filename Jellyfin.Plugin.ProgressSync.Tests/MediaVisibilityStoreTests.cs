using System;
using System.IO;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class MediaVisibilityStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-media-visibility-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void AddAndRemove_PersistRules()
    {
        var path = Path.Combine(_dir, "media-visibility.json");
        var store = NewStore(path);
        var rule = new MediaVisibilityRule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            12,
            1,
            12,
            31,
            DateTime.UtcNow);

        store.Add(rule);

        Assert.Equal(rule, Assert.Single(NewStore(path).Rules));
        Assert.Equal(rule, store.Remove(rule.Id));
        Assert.Empty(NewStore(path).Rules);
        Assert.Null(store.Remove(rule.Id));
    }

    private static MediaVisibilityStore NewStore(string path)
    {
        return new MediaVisibilityStore(path, NullLogger<MediaVisibilityStore>.Instance);
    }
}
