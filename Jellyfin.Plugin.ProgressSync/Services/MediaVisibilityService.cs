using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Applies visibility rules using Jellyfin's per-user blocked-tag access checks.
/// </summary>
public sealed class MediaVisibilityService : IHostedService, IAsyncDisposable
{
    private const string ManagedTagPrefix = "ProgressSync.Hidden.";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly MediaVisibilityStore _store;
    private readonly ILogger<MediaVisibilityService> _logger;
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly HashSet<Guid> _knownRootIds = [];
    private CancellationTokenSource? _stopping;
    private Task? _runTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaVisibilityService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="userManager">The Jellyfin user manager.</param>
    /// <param name="store">The visibility rules.</param>
    /// <param name="logger">The logger.</param>
    public MediaVisibilityService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        MediaVisibilityStore store,
        ILogger<MediaVisibilityService> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        _runTask = RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null || _runTask is null)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies current rules immediately after an admin change.</summary>
    /// <param name="cleanupRootIds">Recently removed rule roots whose generated tags should be cleaned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes after Jellyfin's item and user policies are updated.</returns>
    public async Task ApplyAsync(IEnumerable<Guid>? cleanupRootIds = null, CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rules = _store.Rules;
            var users = _userManager.GetUsers().ToArray();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var itemTags = new Dictionary<Guid, HashSet<string>>();
            var userTags = users.ToDictionary(
                user => user.Id,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var roots = rules.Select(rule => rule.ItemId)
                .Concat(_knownRootIds)
                .Concat(cleanupRootIds ?? [])
                .Distinct()
                .ToArray();

            foreach (var rule in rules)
            {
                if (!rule.IsHiddenOn(today))
                {
                    continue;
                }

                foreach (var user in users)
                {
                    if (rule.UserId is null || rule.UserId == user.Id)
                    {
                        userTags[user.Id].Add(rule.VisibilityTag);
                    }
                }
            }

            foreach (var ruleGroup in rules.GroupBy(rule => rule.ItemId))
            {
                var matchingRules = ruleGroup.ToArray();
                foreach (var item in GetItemScope(ruleGroup.Key))
                {
                    if (!itemTags.TryGetValue(item.Id, out var tags))
                    {
                        tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        itemTags.Add(item.Id, tags);
                    }

                    foreach (var rule in matchingRules)
                    {
                        tags.Add(rule.VisibilityTag);
                    }
                }
            }

            foreach (var rootId in roots)
            {
                foreach (var item in GetItemScope(rootId))
                {
                    var desiredTags = itemTags.GetValueOrDefault(item.Id) ?? [];
                    var updatedTags = MergeManagedTags(item.Tags, desiredTags);
                    if (!item.Tags.Order(StringComparer.OrdinalIgnoreCase)
                        .SequenceEqual(updatedTags.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                    {
                        item.Tags = updatedTags;
                        var parent = item.ParentId == Guid.Empty
                            ? item
                            : _libraryManager.GetItemById<BaseItem>(item.ParentId) ?? item;
                        await _libraryManager.UpdateItemAsync(
                            item,
                            parent,
                            ItemUpdateType.MetadataEdit,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            foreach (var user in users)
            {
                var policy = _userManager.GetUserDto(user, string.Empty).Policy;
                var currentTags = policy.BlockedTags ?? [];
                var updatedTags = MergeManagedTags(currentTags, userTags[user.Id]);
                if (!currentTags.Order(StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(updatedTags.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                {
                    policy.BlockedTags = updatedTags;
                    await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
                }
            }

            _knownRootIds.Clear();
            _knownRootIds.UnionWith(rules.Select(rule => rule.ItemId));
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ApplyAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Progress Sync: could not apply media visibility rules");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private IEnumerable<BaseItem> GetItemScope(Guid rootId)
    {
        var root = _libraryManager.GetItemById<BaseItem>(rootId);
        if (root is null)
        {
            yield break;
        }

        yield return root;
        var descendants = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            ParentId = rootId,
            Recursive = true,
            IsVirtualItem = false
        });
        foreach (var item in descendants.Items)
        {
            if (item.Id != rootId)
            {
                yield return item;
            }
        }
    }

    private static string[] MergeManagedTags(IEnumerable<string>? currentTags, IEnumerable<string> desiredManagedTags)
    {
        return (currentTags ?? [])
            .Where(tag => !tag.StartsWith(ManagedTagPrefix, StringComparison.OrdinalIgnoreCase))
            .Concat(desiredManagedTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _applyLock.Dispose();
        _stopping?.Dispose();
        return ValueTask.CompletedTask;
    }
}
