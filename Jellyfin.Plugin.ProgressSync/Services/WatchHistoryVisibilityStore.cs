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
/// Persists per-viewer Watch History visibility.
/// </summary>
public sealed class WatchHistoryVisibilityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _writeLock = new();
    private readonly string _path;
    private readonly ILogger<WatchHistoryVisibilityStore> _logger;
    private volatile WatchHistoryVisibilitySettings _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchHistoryVisibilityStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server application paths.</param>
    /// <param name="logger">The logger.</param>
    public WatchHistoryVisibilityStore(IApplicationPaths applicationPaths, ILogger<WatchHistoryVisibilityStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ProgressSync", "watch-history-visibility.json"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchHistoryVisibilityStore"/> class with an explicit path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="logger">The logger.</param>
    internal WatchHistoryVisibilityStore(string path, ILogger<WatchHistoryVisibilityStore> logger)
    {
        _path = path;
        _logger = logger;
        _current = Load();
    }

    /// <summary>Gets the current settings snapshot.</summary>
    public WatchHistoryVisibilitySettings Current => _current;

    /// <summary>Checks whether a viewer can see a user's history.</summary>
    /// <param name="viewerId">The viewer's user id.</param>
    /// <param name="userId">The history owner's user id.</param>
    /// <returns>Whether the history is visible.</returns>
    public bool CanView(Guid viewerId, Guid userId)
    {
        var settings = _current;
        if (viewerId == userId || !settings.IsConfigured)
        {
            return true;
        }

        var rule = settings.Rules?.FirstOrDefault(candidate => candidate.UserId == viewerId);
        return rule?.VisibleUserIds.Contains(userId) == true;
    }

    /// <summary>Persists and publishes visibility settings.</summary>
    /// <param name="settings">The settings to save.</param>
    public void Save(WatchHistoryVisibilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var snapshot = new WatchHistoryVisibilitySettings(
            true,
            settings.Rules?.Select(rule => new WatchHistoryVisibilityRule(
                rule.UserId,
                rule.VisibleUserIds.Distinct().ToArray())).ToArray() ?? []);
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

            _current = snapshot;
        }
    }

    private WatchHistoryVisibilitySettings Load()
    {
        if (!File.Exists(_path))
        {
            return new WatchHistoryVisibilitySettings();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<WatchHistoryVisibilitySettings>(stream, JsonOptions)
                ?? new WatchHistoryVisibilitySettings();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Progress Sync: could not read Watch History visibility settings from {Path}", _path);
            return new WatchHistoryVisibilitySettings();
        }
    }
}
