using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ProgressSync.Api;

/// <summary>
/// Admin API for per-user and scheduled media visibility rules.
/// </summary>
[ApiController]
[Route("ProgressSync/Admin/Visibility")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class MediaVisibilityController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly MediaVisibilityStore _store;
    private readonly MediaVisibilityService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaVisibilityController"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="userManager">The Jellyfin user manager.</param>
    /// <param name="store">The visibility rule store.</param>
    /// <param name="service">The visibility rule service.</param>
    public MediaVisibilityController(
        ILibraryManager libraryManager,
        IUserManager userManager,
        MediaVisibilityStore store,
        MediaVisibilityService service)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _store = store;
        _service = service;
    }

    /// <summary>Gets enabled users and configured visibility rules.</summary>
    /// <returns>The current settings.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(MediaVisibilitySettingsResponse), StatusCodes.Status200OK)]
    public ActionResult<MediaVisibilitySettingsResponse> GetSettings()
    {
        var users = GetEnabledUsers()
            .Select(user => new AdminUserDto(user.Id, user.Username))
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rules = _store.Rules
            .Select(ToDto)
            .OrderBy(rule => rule.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MediaVisibilitySettingsResponse(users, rules);
    }

    /// <summary>Searches media items that can be hidden.</summary>
    /// <param name="search">The item name to search for.</param>
    /// <param name="kind">Optional media type filter.</param>
    /// <returns>Matching library items.</returns>
    [HttpGet("Items")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<AdminItemDto>> SearchItems([FromQuery] string? search, [FromQuery] string? kind = null)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2)
        {
            return BadRequest("Enter at least two characters to search.");
        }

        var includeTypes = ResolveSearchKinds(kind);
        var items = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            SearchTerm = search.Trim(),
            Recursive = true,
            Limit = 50,
            IncludeItemTypes = includeTypes.ToArray()
        }).Items
            .Where(IsSupported)
            .Select(item => new AdminItemDto(item.Id, item.Name, item.GetType().Name))
            .ToList();
        return items;
    }

    /// <summary>Adds a media visibility rule and applies it to Jellyfin's user policies.</summary>
    /// <param name="request">The item, target user, and optional annual visible window.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created rule.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(MediaVisibilityRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaVisibilityRuleDto>> AddRule(
        [FromBody] MediaVisibilityRuleRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.ItemId == Guid.Empty || request.UserId == Guid.Empty)
        {
            return BadRequest("Choose a media item and a valid target user.");
        }

        if (!IsValidWindow(request))
        {
            return BadRequest("Provide either no schedule or valid start and end month/day values.");
        }

        var item = _libraryManager.GetItemById<BaseItem>(request.ItemId);
        var user = request.UserId is { } userId ? GetEnabledUser(userId) : null;
        if (item is null || !IsSupported(item) || (request.UserId is not null && user is null))
        {
            return NotFound();
        }

        var rule = new MediaVisibilityRule(
            Guid.NewGuid(),
            item.Id,
            request.UserId,
            request.VisibleFromMonth,
            request.VisibleFromDay,
            request.VisibleThroughMonth,
            request.VisibleThroughDay,
            DateTime.UtcNow);
        _store.Add(rule);
        await _service.ApplyAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return ToDto(rule);
    }

    /// <summary>Removes a media visibility rule and its managed tags.</summary>
    /// <param name="ruleId">The rule id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{ruleId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteRule([FromRoute] Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = _store.Remove(ruleId);
        if (rule is null)
        {
            return NotFound();
        }

        await _service.ApplyAsync([rule.ItemId], cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private IEnumerable<User> GetEnabledUsers()
    {
        return _userManager.GetUsers().Where(user => !user.HasPermission(PermissionKind.IsDisabled));
    }

    private User? GetEnabledUser(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        return user is not null && !user.HasPermission(PermissionKind.IsDisabled) ? user : null;
    }

    private MediaVisibilityRuleDto ToDto(MediaVisibilityRule rule)
    {
        var item = _libraryManager.GetItemById<BaseItem>(rule.ItemId);
        var user = rule.UserId is { } userId ? _userManager.GetUserById(userId) : null;
        return new MediaVisibilityRuleDto(
            rule.Id,
            rule.ItemId,
            item?.Name ?? "(missing item)",
            item?.GetType().Name ?? "Unknown",
            rule.UserId,
            user?.Username,
            rule.VisibleFromMonth,
            rule.VisibleFromDay,
            rule.VisibleThroughMonth,
            rule.VisibleThroughDay,
            rule.CreatedUtc);
    }

    private static List<BaseItemKind> ResolveSearchKinds(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return
            [
                BaseItemKind.Movie,
                BaseItemKind.Series,
                BaseItemKind.Season,
                BaseItemKind.Episode,
                BaseItemKind.Audio,
                BaseItemKind.MusicAlbum,
                BaseItemKind.MusicArtist
            ];
        }

        return kind.Trim() switch
        {
            "movie" or "movies" => [BaseItemKind.Movie],
            "series" or "show" or "shows" => [BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode],
            "music" or "audio" or "album" or "albums" => [BaseItemKind.Audio, BaseItemKind.MusicAlbum, BaseItemKind.MusicArtist],
            _ => [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode, BaseItemKind.Audio, BaseItemKind.MusicAlbum, BaseItemKind.MusicArtist]
        };
    }

    private static bool IsSupported(BaseItem item)
    {
        return item is Movie or Series or Season or Episode or Audio or MusicAlbum or MusicArtist;
    }

    private static bool IsValidWindow(MediaVisibilityRuleRequest request)
    {
        var values = new[]
        {
            request.VisibleFromMonth,
            request.VisibleFromDay,
            request.VisibleThroughMonth,
            request.VisibleThroughDay
        };
        if (values.All(value => value is null))
        {
            return true;
        }

        return values.All(value => value is not null)
            && IsValidMonthDay(request.VisibleFromMonth!.Value, request.VisibleFromDay!.Value)
            && IsValidMonthDay(request.VisibleThroughMonth!.Value, request.VisibleThroughDay!.Value);
    }

    private static bool IsValidMonthDay(int month, int day)
    {
        return month is >= 1 and <= 12
            && day >= 1
            && day <= DateTime.DaysInMonth(2000, month);
    }
}
