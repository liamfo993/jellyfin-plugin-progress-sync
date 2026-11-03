using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Persists Pushover credentials and notification preferences.
/// </summary>
public sealed class PushoverNotificationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _writeLock = new();
    private readonly string _path;
    private readonly ILogger<PushoverNotificationStore> _logger;
    private volatile PushoverNotificationSettings _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="PushoverNotificationStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server application paths.</param>
    /// <param name="logger">The logger.</param>
    public PushoverNotificationStore(IApplicationPaths applicationPaths, ILogger<PushoverNotificationStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ProgressSync", "pushover-notifications.json"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PushoverNotificationStore"/> class with an explicit path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="logger">The logger.</param>
    internal PushoverNotificationStore(string path, ILogger<PushoverNotificationStore> logger)
    {
        _path = path;
        _logger = logger;
        _current = Load();
    }

    /// <summary>Gets the current settings snapshot.</summary>
    public PushoverNotificationSettings Current => _current;

    /// <summary>Updates settings and preserves credentials when replacement fields are blank.</summary>
    /// <param name="update">The requested changes.</param>
    public void Save(PushoverNotificationsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_writeLock)
        {
            var current = _current;
            var settings = new PushoverNotificationSettings
            {
                AppToken = update.ClearCredentials
                    ? null
                    : NormalizeCredential(update.AppToken) ?? current.AppToken,
                UserKey = update.ClearCredentials
                    ? null
                    : NormalizeCredential(update.UserKey) ?? current.UserKey,
                NotifyAdded = update.NotifyAdded,
                NotifyWatched = update.NotifyWatched,
                NotifyDeleted = update.NotifyDeleted,
                NotifyPlaybackStarted = update.NotifyPlaybackStarted,
                NotifyPlaybackStopped = update.NotifyPlaybackStopped
            };

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

    private static string? NormalizeCredential(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private PushoverNotificationSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new PushoverNotificationSettings();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<PushoverNotificationSettings>(stream, JsonOptions)
                ?? new PushoverNotificationSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Progress Sync: could not read Pushover notification settings from {Path}", _path);
            return new PushoverNotificationSettings();
        }
    }
}
