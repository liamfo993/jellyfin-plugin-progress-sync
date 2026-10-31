using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Persists and publishes global sync settings.
/// </summary>
public sealed class GlobalSyncSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _writeLock = new();
    private readonly string _path;
    private volatile GlobalSyncSettings _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalSyncSettingsStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server application paths.</param>
    public GlobalSyncSettingsStore(IApplicationPaths applicationPaths)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ProgressSync", "global-settings.json"))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalSyncSettingsStore"/> class with an explicit path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    internal GlobalSyncSettingsStore(string path)
    {
        _path = path;
        _current = Load();
    }

    /// <summary>Gets the current settings snapshot.</summary>
    public GlobalSyncSettings Current => _current;

    /// <summary>Persists and publishes settings.</summary>
    /// <param name="settings">The settings to save.</param>
    public void Save(GlobalSyncSettings settings)
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

    private GlobalSyncSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new GlobalSyncSettings();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<GlobalSyncSettings>(stream) ?? new GlobalSyncSettings();
        }
        catch (JsonException)
        {
            return new GlobalSyncSettings();
        }
    }
}
