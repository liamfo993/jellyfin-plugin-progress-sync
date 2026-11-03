using System;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class ConcurrentStreamLimitStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-stream-limit-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void Save_PersistsLimitsAndOmittingAUserRemovesTheirLimit()
    {
        var path = Path.Combine(_dir, "stream-limits.json");
        var userId = Guid.NewGuid();
        var store = NewStore(path);

        store.Save([new ConcurrentStreamLimit(userId, 2, StopOldest: true)]);

        Assert.Equal(2, store.Limits[userId].MaxConcurrentStreams);
        Assert.True(store.Limits[userId].StopOldest);
        Assert.Equal(2, NewStore(path).Limits[userId].MaxConcurrentStreams);
        Assert.True(NewStore(path).Limits[userId].StopOldest);

        store.Save([new ConcurrentStreamLimit(userId, 0, StopOldest: true)]);

        Assert.Equal(0, store.Limits[userId].MaxConcurrentStreams);
        Assert.True(NewStore(path).Limits[userId].StopOldest);

        store.Save([new ConcurrentStreamLimit(userId, 0)]);

        Assert.Empty(store.Limits);
        Assert.Empty(NewStore(path).Limits);
    }

    [Fact]
    public void Load_ReadsLegacyIntegerSettingsAndDefaultsToNewest()
    {
        var path = Path.Combine(_dir, "stream-limits.json");
        var userId = Guid.NewGuid();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, JsonSerializer.Serialize(new System.Collections.Generic.Dictionary<Guid, int>
        {
            [userId] = 3
        }));

        var store = NewStore(path);

        Assert.Equal(3, store.Limits[userId].MaxConcurrentStreams);
        Assert.False(store.Limits[userId].StopOldest);
    }

    private static ConcurrentStreamLimitStore NewStore(string path)
    {
        return new ConcurrentStreamLimitStore(path, NullLogger<ConcurrentStreamLimitStore>.Instance);
    }
}
