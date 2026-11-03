using System;
using System.IO;
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

        store.Save([new ConcurrentStreamLimit(userId, 2)]);

        Assert.Equal(2, store.Limits[userId]);
        Assert.Equal(2, NewStore(path).Limits[userId]);

        store.Save([new ConcurrentStreamLimit(userId, 0)]);

        Assert.Empty(store.Limits);
        Assert.Empty(NewStore(path).Limits);
    }

    private static ConcurrentStreamLimitStore NewStore(string path)
    {
        return new ConcurrentStreamLimitStore(path, NullLogger<ConcurrentStreamLimitStore>.Instance);
    }
}
