using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Persists whether Watch History is available from the Jellyfin user menu.
/// </summary>
public sealed class WatchHistoryMenuStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _writeLock = new();
    private readonly string _path;
    private readonly ILogger<WatchHistoryMenuStore> _logger;
    private volatile WatchHistoryMenuSettings _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchHistoryMenuStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server application paths.</param>
    /// <param name="logger">The logger.</param>
    public WatchHistoryMenuStore(IApplicationPaths applicationPaths, ILogger<WatchHistoryMenuStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ProgressSync", "watch-history-menu.json"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchHistoryMenuStore"/> class with an explicit path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="logger">The logger.</param>
    internal WatchHistoryMenuStore(string path, ILogger<WatchHistoryMenuStore> logger)
    {
        _path = path;
        _logger = logger;
        _current = Load();
    }

    /// <summary>Gets the current settings snapshot.</summary>
    public WatchHistoryMenuSettings Current => _current;

    /// <summary>Updates the user-menu setting without changing other settings.</summary>
    /// <param name="enabled">Whether the menu entry is enabled.</param>
    public void SetMenuEnabled(bool enabled)
    {
        lock (_writeLock)
        {
            Save(_current with { Enabled = enabled });
        }
    }

    /// <summary>Updates the Ratings integration without changing other settings.</summary>
    /// <param name="enabled">Whether rating badges are enabled.</param>
    public void SetRatingsEnabled(bool enabled)
    {
        lock (_writeLock)
        {
            Save(_current with { RatingsEnabled = enabled });
        }
    }

    /// <summary>Persists and publishes settings.</summary>
    /// <param name="settings">The settings to save.</param>
    public void Save(WatchHistoryMenuSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
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

            _current = settings;
        }
    }

    private WatchHistoryMenuSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new WatchHistoryMenuSettings();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<WatchHistoryMenuSettings>(stream, JsonOptions)
                ?? new WatchHistoryMenuSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Progress Sync: could not read Watch History menu settings from {Path}", _path);
            return new WatchHistoryMenuSettings();
        }
    }
}
