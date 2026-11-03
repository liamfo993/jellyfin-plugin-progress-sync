using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Persists and publishes per-user concurrent-stream limits.
/// </summary>
public sealed class ConcurrentStreamLimitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _writeLock = new();
    private readonly string _path;
    private readonly ILogger<ConcurrentStreamLimitStore> _logger;
    private volatile IReadOnlyDictionary<Guid, int> _limits;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentStreamLimitStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server application paths.</param>
    /// <param name="logger">The logger.</param>
    public ConcurrentStreamLimitStore(IApplicationPaths applicationPaths, ILogger<ConcurrentStreamLimitStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ProgressSync", "stream-limits.json"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentStreamLimitStore"/> class with an explicit path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="logger">The logger.</param>
    internal ConcurrentStreamLimitStore(string path, ILogger<ConcurrentStreamLimitStore> logger)
    {
        _path = path;
        _logger = logger;
        _limits = Load();
    }

    /// <summary>Gets the current limits snapshot.</summary>
    public IReadOnlyDictionary<Guid, int> Limits => _limits;

    /// <summary>Replaces all configured limits.</summary>
    /// <param name="limits">The per-user limits to persist.</param>
    public void Save(IEnumerable<ConcurrentStreamLimit> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var snapshot = limits
            .Where(limit => limit.MaxConcurrentStreams > 0)
            .ToDictionary(limit => limit.UserId, limit => limit.MaxConcurrentStreams);
        lock (_writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, snapshot, JsonOptions);
                stream.Flush(true);
            }

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, null);
            }
            else
            {
                File.Move(tempPath, _path);
            }

            _limits = snapshot;
        }
    }

    private IReadOnlyDictionary<Guid, int> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<Guid, int>();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<Dictionary<Guid, int>>(stream, JsonOptions)
                ?? new Dictionary<Guid, int>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Progress Sync: could not read concurrent stream limits from {Path}", _path);
            return new Dictionary<Guid, int>();
        }
    }
}
