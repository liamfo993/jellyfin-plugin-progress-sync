using System;
using System.IO;
using System.Linq;
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
    private static readonly Guid LegacyProfileId = new("2ce8cb7d-6a1e-4e16-a3f1-0c7ca7b6868b");
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
        settings = settings with { Profiles = settings.Profiles?.ToArray() ?? [] };
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

    /// <summary>Adds a profile without overwriting concurrent profile changes.</summary>
    /// <param name="profile">The profile to add.</param>
    public void AddProfile(GlobalSyncProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_writeLock)
        {
            Save(_current with { Profiles = _current.Profiles.Append(profile).ToArray() });
        }
    }

    /// <summary>Removes a profile without overwriting concurrent profile changes.</summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <returns>The removed profile, or <c>null</c> if it did not exist.</returns>
    public GlobalSyncProfile? RemoveProfile(Guid profileId)
    {
        lock (_writeLock)
        {
            var settings = _current;
            var profile = settings.Profiles.FirstOrDefault(candidate => candidate.Id == profileId);
            if (profile is null)
            {
                return null;
            }

            Save(settings with
            {
                Profiles = settings.Profiles.Where(candidate => candidate.Id != profileId).ToArray()
            });
            return profile;
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
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new GlobalSyncSettings();
            }

            var settings = JsonSerializer.Deserialize<GlobalSyncSettings>(document.RootElement.GetRawText(), JsonOptions)
                ?? new GlobalSyncSettings();
            if (settings.Profiles is { Count: > 0 })
            {
                return settings;
            }

            var root = document.RootElement;
            var sourceUserId = ReadGuid(root, "UserAId");
            var targetUserId = ReadGuid(root, "UserBId");
            var legacyProfile = new GlobalSyncProfile
            {
                Id = LegacyProfileId,
                SourceUserId = sourceUserId,
                TargetUserId = targetUserId,
                Mode = LinkMode.Mirror,
                Movies = ReadBool(root, "Movies"),
                Series = ReadBool(root, "Series"),
                Music = ReadBool(root, "Music"),
                CreatedUtc = DateTime.UtcNow
            };
            return legacyProfile.IsActive
                ? new GlobalSyncSettings { Profiles = [legacyProfile] }
                : settings;
        }
        catch (JsonException)
        {
            return new GlobalSyncSettings();
        }
    }

    private static Guid ReadGuid(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var id)
                ? id
                : Guid.Empty;
    }

    private static bool ReadBool(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
    }
}
