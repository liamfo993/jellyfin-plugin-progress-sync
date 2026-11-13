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
/// Persists and publishes per-user and global media visibility rules.
/// </summary>
public sealed class MediaVisibilityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _writeLock = new();
    private readonly string _path;
    private readonly ILogger<MediaVisibilityStore> _logger;
    private volatile IReadOnlyList<MediaVisibilityRule> _rules;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaVisibilityStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server application paths.</param>
    /// <param name="logger">The logger.</param>
    public MediaVisibilityStore(IApplicationPaths applicationPaths, ILogger<MediaVisibilityStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ProgressSync", "media-visibility.json"), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaVisibilityStore"/> class with an explicit path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="logger">The logger.</param>
    internal MediaVisibilityStore(string path, ILogger<MediaVisibilityStore> logger)
    {
        _path = path;
        _logger = logger;
        _rules = Load();
    }

    /// <summary>Gets the current immutable rules snapshot.</summary>
    public IReadOnlyList<MediaVisibilityRule> Rules => _rules;

    /// <summary>Adds and persists a rule.</summary>
    /// <param name="rule">The rule to add.</param>
    public void Add(MediaVisibilityRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_writeLock)
        {
            Save(_rules.Append(rule).ToArray());
        }
    }

    /// <summary>Adds and persists several rules together.</summary>
    /// <param name="rules">The rules to add.</param>
    public void AddRange(IEnumerable<MediaVisibilityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var newRules = rules.ToArray();
        if (newRules.Length == 0)
        {
            return;
        }

        lock (_writeLock)
        {
            Save(_rules.Concat(newRules).ToArray());
        }
    }

    /// <summary>Removes and persists a rule.</summary>
    /// <param name="ruleId">The rule id.</param>
    /// <returns>The removed rule, or <see langword="null"/> when it did not exist.</returns>
    public MediaVisibilityRule? Remove(Guid ruleId)
    {
        lock (_writeLock)
        {
            var rule = _rules.FirstOrDefault(candidate => candidate.Id == ruleId);
            if (rule is null)
            {
                return null;
            }

            Save(_rules.Where(candidate => candidate.Id != ruleId).ToArray());
            return rule;
        }
    }

    private void Save(IReadOnlyList<MediaVisibilityRule> rules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tempPath = _path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, rules, JsonOptions);
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

        _rules = rules.ToArray();
    }

    private IReadOnlyList<MediaVisibilityRule> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<MediaVisibilityRule[]>(stream, JsonOptions)
                ?? throw new JsonException("Media visibility rules must be a JSON array.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Progress Sync: could not read media visibility rules from {Path}", _path);
            return [];
        }
    }
}
