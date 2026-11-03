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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Lock _writeLock = new();
    private readonly string _path;
    private readonly ILogger<ConcurrentStreamLimitStore> _logger;
    private volatile IReadOnlyDictionary<Guid, ConcurrentStreamLimitSetting> _limits;

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
    public IReadOnlyDictionary<Guid, ConcurrentStreamLimitSetting> Limits => _limits;

    /// <summary>Replaces all configured limits.</summary>
    /// <param name="limits">The per-user limits to persist.</param>
    public void Save(IEnumerable<ConcurrentStreamLimit> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var snapshot = limits
            .Where(limit => limit.MaxConcurrentStreams > 0 || limit.StopOldest)
            .ToDictionary(
                limit => limit.UserId,
                limit => new ConcurrentStreamLimitSetting(limit.MaxConcurrentStreams, limit.StopOldest));
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

    private IReadOnlyDictionary<Guid, ConcurrentStreamLimitSetting> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<Guid, ConcurrentStreamLimitSetting>();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Concurrent stream limits must be a JSON object.");
            }

            var result = new Dictionary<Guid, ConcurrentStreamLimitSetting>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!Guid.TryParse(property.Name, out var userId))
                {
                    throw new JsonException("Concurrent stream limit contains an invalid user ID.");
                }

                if (property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out var legacyLimit)
                    && legacyLimit > 0)
                {
                    result[userId] = new ConcurrentStreamLimitSetting(legacyLimit, false);
                    continue;
                }

                var setting = property.Value.Deserialize<ConcurrentStreamLimitSetting>(JsonOptions);
                if (setting is null || setting.MaxConcurrentStreams < 0)
                {
                    throw new JsonException("Concurrent stream limit contains an invalid setting.");
                }

                if (setting.MaxConcurrentStreams > 0 || setting.StopOldest)
                {
                    result[userId] = setting;
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Progress Sync: could not read concurrent stream limits from {Path}", _path);
            return new Dictionary<Guid, ConcurrentStreamLimitSetting>();
        }
    }
}
