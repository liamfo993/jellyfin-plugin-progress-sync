using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Api;

/// <summary>
/// REST API for the Progress Sync dialog and admin page.
/// </summary>
[ApiController]
[Route("ProgressSync")]
[Authorize]
public sealed class ProgressSyncController : ControllerBase
{
    private const int RecentlyWatchedPerTypeLimit = 25;

    // Jellyfin.Api.Constants.InternalClaimTypes.UserId; that assembly is not available to plugins.
    private const string UserIdClaimType = "Jellyfin-UserId";
    private const string ClientScriptResource = "Jellyfin.Plugin.ProgressSync.Web.progressSync.js";
    private const string WatchHistoryMenuScriptResource = "Jellyfin.Plugin.ProgressSync.Web.watchHistoryMenu.js";

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ProgressCopier _copier;
    private readonly LinkStore _store;
    private readonly GlobalSyncSettingsStore _globalSettings;
    private readonly ConcurrentStreamLimitStore _streamLimits;
    private readonly PushoverNotificationStore _pushoverSettings;
    private readonly PushoverNotificationService _pushoverService;
    private readonly WebInjectionStatus _injectionStatus;
    private readonly WatchHistoryMenuStore _watchHistoryMenu;
    private readonly ILogger<ProgressSyncController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressSyncController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="copier">The progress copier.</param>
    /// <param name="store">The link store.</param>
    /// <param name="globalSettings">The global sync settings store.</param>
    /// <param name="streamLimits">Per-user concurrent stream limits.</param>
    /// <param name="pushoverSettings">The Pushover notification settings.</param>
    /// <param name="pushoverService">The Pushover notification service.</param>
    /// <param name="injectionStatus">The web client integration status.</param>
    /// <param name="watchHistoryMenu">Whether Watch History is shown in Jellyfin's user menu.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ProgressSyncController}"/> interface.</param>
    public ProgressSyncController(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ProgressCopier copier,
        LinkStore store,
        GlobalSyncSettingsStore globalSettings,
        ConcurrentStreamLimitStore streamLimits,
        PushoverNotificationStore pushoverSettings,
        PushoverNotificationService pushoverService,
        WebInjectionStatus injectionStatus,
        WatchHistoryMenuStore watchHistoryMenu,
        ILogger<ProgressSyncController> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _copier = copier;
        _store = store;
        _globalSettings = globalSettings;
        _streamLimits = streamLimits;
        _pushoverSettings = pushoverSettings;
        _pushoverService = pushoverService;
        _injectionStatus = injectionStatus;
        _watchHistoryMenu = watchHistoryMenu;
        _logger = logger;
    }

    /// <summary>
    /// Copies another user's watch progress to the calling user, once.
    /// </summary>
    /// <param name="itemId">A movie, episode, season or series.</param>
    /// <param name="sourceUserId">The user to copy from.</param>
    /// <response code="200">Progress copied.</response>
    /// <response code="400">Unsupported item type or copying from yourself.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Item or source user not found or not accessible.</response>
    /// <returns>The copy result.</returns>
    [HttpPost("Items/{itemId}/CopyFrom/{sourceUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CopyResult> CopyFrom([FromRoute] Guid itemId, [FromRoute] Guid sourceUserId)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (sourceUserId.Equals(me.Id))
        {
            return BadRequest("Cannot copy progress from yourself.");
        }

        var source = GetEnabledUser(sourceUserId);
        var item = GetVisibleItem(itemId, me);
        if (source is null || item is null)
        {
            return NotFound();
        }

        if (!IsSupported(item))
        {
            return BadRequest("Unsupported item type.");
        }

        return _copier.CopyScope(source, me, item, UserDataSaveReason.UpdateUserData);
    }

    /// <summary>
    /// Lists enabled users' watch status for a movie or episode, respecting each user's item access.
    /// </summary>
    /// <param name="itemId">A movie or episode.</param>
    /// <response code="200">Watch status returned.</response>
    /// <response code="400">Item is not a movie or episode.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Item not found or not accessible.</response>
    /// <returns>The watch status for enabled users.</returns>
    [HttpGet("Items/{itemId}/WatchStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<UserWatchStatusDto>> GetWatchStatus([FromRoute] Guid itemId)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var item = GetVisibleItem(itemId, me);
        if (item is null)
        {
            return NotFound();
        }

        if (item is not Movie and not Episode)
        {
            return BadRequest("Watch status is only available for movies and episodes.");
        }

        var statuses = _userManager.GetUsers()
            .Where(user => !user.HasPermission(PermissionKind.IsDisabled))
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => GetWatchStatus(user, [item]))
            .ToList();

        return statuses;
    }

    /// <summary>
    /// Lists the enabled users available in the watch history dashboard.
    /// </summary>
    /// <response code="200">Users returned.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <returns>Enabled users ordered by name.</returns>
    [HttpGet("RecentlyWatched/Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IReadOnlyList<RecentlyWatchedUserDto>> GetRecentlyWatchedUsers()
    {
        if (GetCurrentUser() is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return _userManager.GetUsers()
            .Where(user => !user.HasPermission(PermissionKind.IsDisabled))
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => new RecentlyWatchedUserDto(user.Id, user.Username))
            .ToList();
    }

    /// <summary>
    /// Lists a user's 25 most recent completed movie and episode watches of each type that the caller can access.
    /// </summary>
    /// <param name="userId">The user whose watch history to list.</param>
    /// <response code="200">Watch history returned.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">The selected user is not found or is disabled.</response>
    /// <returns>The user's items, newest first.</returns>
    [HttpGet("RecentlyWatched/Users/{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<RecentlyWatchedItemDto>> GetUserRecentlyWatched([FromRoute] Guid userId)
    {
        var caller = GetCurrentUser();
        if (caller is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var user = GetEnabledUser(userId);
        if (user is null)
        {
            return NotFound();
        }

        return GetUserHistory(user, caller, inProgress: false).ToList();
    }

    /// <summary>
    /// Lists a user's 25 most recent partially watched movies and episodes of each type that the caller can access.
    /// </summary>
    /// <param name="userId">The user whose in-progress history to list.</param>
    /// <response code="200">In-progress items returned.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">The selected user is not found or is disabled.</response>
    /// <returns>The user's in-progress items, newest first.</returns>
    [HttpGet("InProgress/Users/{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<RecentlyWatchedItemDto>> GetUserInProgress([FromRoute] Guid userId)
    {
        var caller = GetCurrentUser();
        if (caller is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var user = GetEnabledUser(userId);
        if (user is null)
        {
            return NotFound();
        }

        return GetUserHistory(user, caller, inProgress: true).ToList();
    }

    private IReadOnlyList<RecentlyWatchedItemDto> GetUserHistory(User user, User caller, bool inProgress)
    {
        var items = new List<RecentlyWatchedItemDto>(RecentlyWatchedPerTypeLimit * 2);
        var movieCount = 0;
        var episodeCount = 0;
        var startIndex = 0;
        while (movieCount < RecentlyWatchedPerTypeLimit || episodeCount < RecentlyWatchedPerTypeLimit)
        {
            var result = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
            {
                Recursive = true,
                Limit = RecentlyWatchedPerTypeLimit,
                StartIndex = startIndex,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
                IsPlayed = !inProgress,
                IsVirtualItem = false,
                EnableTotalRecordCount = false,
                OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)]
            });
            if (result.Items.Count == 0)
            {
                break;
            }

            startIndex += result.Items.Count;
            var playedItems = result.Items.Where(item => item is Movie or Episode).ToList();
            var userData = _userDataManager.GetUserDataBatch(playedItems, user);
            foreach (var item in playedItems)
            {
                if ((item is Movie && movieCount >= RecentlyWatchedPerTypeLimit)
                    || (item is Episode && episodeCount >= RecentlyWatchedPerTypeLimit))
                {
                    continue;
                }

                var data = userData.GetValueOrDefault(item.Id);
                var lastPlayedDate = data?.LastPlayedDate;
                var playbackPositionTicks = data?.PlaybackPositionTicks ?? 0;
                if (lastPlayedDate is null
                    || (inProgress && (data?.Played != false || playbackPositionTicks <= 0))
                    || (!inProgress && data?.Played != true)
                    || GetVisibleItem(item.Id, caller) is null)
                {
                    continue;
                }

                var itemName = item.Name;
                Guid? seriesId = null;
                if (item is Episode episode)
                {
                    var visibleSeries = episode.SeriesId == Guid.Empty
                        ? null
                        : GetVisibleItem(episode.SeriesId, caller);
                    itemName = FormatEpisodeName(episode, visibleSeries?.Name);
                    seriesId = visibleSeries?.Id;
                }

                int? progressPercent = inProgress && item.RunTimeTicks is > 0
                    ? Math.Clamp((int)Math.Round((double)playbackPositionTicks / item.RunTimeTicks.Value * 100), 0, 99)
                    : null;
                items.Add(new RecentlyWatchedItemDto(
                    item.Id,
                    itemName,
                    item is Episode ? "Episode" : "Movie",
                    lastPlayedDate.Value,
                    seriesId,
                    progressPercent,
                    inProgress));
                if (item is Movie)
                {
                    movieCount++;
                }
                else
                {
                    episodeCount++;
                }
            }

            if (result.Items.Count < RecentlyWatchedPerTypeLimit)
            {
                break;
            }
        }

        return items;
    }

    private static string FormatEpisodeName(Episode episode, string? seriesName)
    {
        var parts = new List<string>(3);
        if (string.IsNullOrWhiteSpace(seriesName) && episode.SeriesId == Guid.Empty)
        {
            seriesName = episode.SeriesName;
        }

        if (!string.IsNullOrWhiteSpace(seriesName))
        {
            parts.Add(seriesName);
        }

        if (episode.ParentIndexNumber.HasValue && episode.IndexNumber.HasValue)
        {
            parts.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"S{episode.ParentIndexNumber.Value:00}E{episode.IndexNumber.Value:00}"));
        }
        else if (episode.IndexNumber.HasValue)
        {
            parts.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Episode {episode.IndexNumber.Value}"));
        }

        if (!string.IsNullOrWhiteSpace(episode.Name))
        {
            parts.Add(episode.Name);
        }

        return string.Join(" - ", parts);
    }

    /// <summary>
    /// Lists the links on an item or its series that the calling user can see.
    /// </summary>
    /// <param name="itemId">A movie, episode, season or series.</param>
    /// <response code="200">Links returned.</response>
    /// <response code="400">Item cannot be linked.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Item not found or not accessible.</response>
    /// <returns>The visible links.</returns>
    [HttpGet("Items/{itemId}/Links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<LinkDto>> GetLinks([FromRoute] Guid itemId)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var item = GetVisibleItem(itemId, me);
        if (item is null)
        {
            return NotFound();
        }

        var rootId = LinkRules.ResolveRoot(item);
        if (rootId is null && item is not Episode)
        {
            return BadRequest("Item cannot be linked.");
        }

        var snapshot = _store.Snapshot;
        var links = rootId is { } id ? snapshot.ForRoot(id) : [];
        var episodeLinks = item is Episode && item.Id != rootId
            ? snapshot.ForRoot(item.Id)
            : [];
        return links.Concat(episodeLinks)
            .Where(l => LinkRules.IsVisibleTo(l, me.Id))
            .Select(l => ToDto(l, me.Id, item is Episode && l.RootItemId == item.Id))
            .OfType<LinkDto>()
            .OrderBy(l => l.PartnerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Creates (or changes the mode of) a link on an item's series or movie, then copies the partner's
    /// progress to the calling user.
    /// </summary>
    /// <param name="itemId">A movie, episode, season or series.</param>
    /// <param name="request">Partner and mode.</param>
    /// <response code="200">Link stored; see the initial copy fields for the copy outcome.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Item or partner not found or not accessible to both users.</response>
    /// <returns>The link and the initial copy result.</returns>
    [HttpPost("Items/{itemId}/Links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CreateLinkResponse> CreateLink([FromRoute] Guid itemId, [FromBody] CreateLinkRequest request)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request?.Mode is not { } mode || !Enum.IsDefined(mode) || request.PartnerUserId.Equals(Guid.Empty))
        {
            return BadRequest("PartnerUserId and Mode are required.");
        }

        if (request.PartnerUserId.Equals(me.Id))
        {
            return BadRequest("Cannot link with yourself.");
        }

        var partner = GetEnabledUser(request.PartnerUserId);
        var item = GetVisibleItem(itemId, me);
        if (partner is null || item is null)
        {
            return NotFound();
        }

        if (LinkRules.ResolveRoot(item) is not { } rootId)
        {
            return BadRequest("Item cannot be linked.");
        }

        var rootItem = GetVisibleItem(rootId, me);
        if (rootItem is null || GetVisibleItem(rootId, partner) is null)
        {
            return NotFound();
        }

        var link = _store.Upsert(me.Id, partner.Id, rootId, mode, BuildStaleCheck());
        _logger.LogInformation(
            "Progress Sync: {User} created {Mode} link with {Partner} on {ItemName} ({RootId})",
            me.Username,
            mode,
            partner.Username,
            rootItem.Name,
            rootId);

        CopyResult? copy = null;
        string? copyError = null;
        try
        {
            copy = _copier.CopyScope(partner, me, rootItem, UserDataSaveReason.UpdateUserData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: initial copy for link {LinkId} failed", link.Id);
            copyError = ex.Message;
        }

        return new CreateLinkResponse(new LinkDto(link.Id, partner.Id, partner.Username, link.Mode, true, true, link.CreatedUtc), copy, copyError);
    }

    /// <summary>
    /// Removes a link. Creators can remove their links; partners can remove two-way links.
    /// </summary>
    /// <param name="linkId">The link id.</param>
    /// <response code="204">Link removed.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Link not found or not removable by the caller.</response>
    /// <returns>No content.</returns>
    [HttpDelete("Links/{linkId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteLink([FromRoute] Guid linkId)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var link = _store.Snapshot.Find(linkId);
        if (link is null || !LinkRules.CanRemove(link, me.Id))
        {
            return NotFound();
        }

        _store.Remove(linkId, BuildStaleCheck());
        _logger.LogInformation("Progress Sync: {User} removed {Mode} link {LinkId} on {RootId}", me.Username, link.Mode, link.Id, link.RootItemId);
        return NoContent();
    }

    /// <summary>
    /// Serves the web client script. Anonymous because index.html loads it before sign-in.
    /// </summary>
    /// <param name="v">Script version from the injected URL.</param>
    /// <response code="200">Script returned.</response>
    /// <returns>The script.</returns>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetClientScript([FromQuery] string? v)
    {
        var stream = typeof(ProgressSyncController).Assembly.GetManifestResourceStream(ClientScriptResource);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = string.Equals(v, IndexTransformer.ScriptVersion, StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        return File(stream, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Gets the public menu visibility setting.
    /// </summary>
    /// <returns>The current menu visibility setting.</returns>
    [HttpGet("WatchHistoryMenu")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<WatchHistoryMenuSettings> GetWatchHistoryMenuSettings()
    {
        return _watchHistoryMenu.Current;
    }

    /// <summary>
    /// Serves the user-facing Watch History dialog script.
    /// </summary>
    /// <param name="v">Script version from the injected URL.</param>
    /// <response code="200">Script returned.</response>
    /// <returns>The script.</returns>
    [HttpGet("WatchHistoryMenuScript")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetWatchHistoryMenuScript([FromQuery] string? v)
    {
        var stream = typeof(ProgressSyncController).Assembly.GetManifestResourceStream(WatchHistoryMenuScriptResource);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = string.Equals(v, IndexTransformer.ScriptVersion, StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        return File(stream, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Updates whether Watch History is shown in Jellyfin's user menu.
    /// </summary>
    /// <param name="settings">The menu visibility setting.</param>
    /// <returns>No content when saved.</returns>
    [HttpPost("Admin/WatchHistoryMenu")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult UpdateWatchHistoryMenuSettings([FromBody] WatchHistoryMenuSettings? settings)
    {
        if (settings is null)
        {
            return BadRequest("Settings are required.");
        }

        _watchHistoryMenu.Save(settings);
        _logger.LogInformation("Progress Sync: administrator {State} the Watch History user-menu link", settings.Enabled ? "enabled" : "disabled");
        return NoContent();
    }

    /// <summary>
    /// Integration status and all links, for the admin page.
    /// </summary>
    /// <response code="200">Overview returned.</response>
    /// <returns>The overview.</returns>
    [HttpGet("Admin/Overview")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AdminOverviewDto> GetAdminOverview()
    {
        var links = _store.Snapshot.All
            .Select(l => new AdminLinkDto(
                l.Id,
                _userManager.GetUserById(l.CreatorUserId)?.Username ?? "(deleted)",
                _userManager.GetUserById(l.PartnerUserId)?.Username ?? "(deleted)",
                _libraryManager.GetItemById(l.RootItemId) is { } item ? AdminItemName(item) : "(missing)",
                l.RootItemId,
                l.Mode,
                l.CreatedUtc))
            .OrderBy(l => l.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.CreatorName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AdminOverviewDto(_injectionStatus.State, _injectionStatus.Message, links);
    }

    /// <summary>
    /// Searches movies, series, and music for admin sync setup.
    /// </summary>
    /// <param name="search">The item name to search for.</param>
    /// <param name="kind">Optional media type filter.</param>
    /// <param name="musicItems">The music item grouping.</param>
    /// <param name="sort">The result ordering.</param>
    /// <param name="seriesItems">The series item grouping.</param>
    /// <returns>Matching library items.</returns>
    [HttpGet("Admin/Items")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<AdminItemDto>> SearchAdminItems(
        [FromQuery] string? search,
        [FromQuery] string? kind = null,
        [FromQuery] string? musicItems = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? seriesItems = null)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2)
        {
            return BadRequest("Enter at least two characters to search.");
        }

        var includeTypes = ResolveAdminSearchKinds(kind, musicItems, seriesItems);
        var items = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            SearchTerm = search.Trim(),
            Recursive = true,
            Limit = 50,
            IncludeItemTypes = includeTypes.ToArray(),
            EnableTotalRecordCount = false,
            OrderBy = [ResolveAdminItemSort(sort)]
        }).Items
            .Where(item => !item.IsVirtualItem)
            .Select(item => new AdminItemDto(item.Id, AdminItemName(item), item.GetType().Name))
            .ToList();

        return items;
    }

    /// <summary>
    /// Browses a page of movies, series and music for admin sync setup.
    /// </summary>
    /// <param name="kind">Optional media type filter.</param>
    /// <param name="musicItems">The music item grouping.</param>
    /// <param name="sort">The result ordering.</param>
    /// <param name="startIndex">The zero-based index of the first item.</param>
    /// <param name="seriesItems">The series item grouping.</param>
    /// <returns>A page of items and whether more are available.</returns>
    [HttpGet("Admin/Browse")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(typeof(AdminItemBrowseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<AdminItemBrowseResponse> BrowseAdminItems(
        [FromQuery] string? kind = null,
        [FromQuery] string? musicItems = null,
        [FromQuery] string? sort = null,
        [FromQuery] int startIndex = 0,
        [FromQuery] string? seriesItems = null)
    {
        if (startIndex < 0)
        {
            return BadRequest("The start index cannot be negative.");
        }

        const int pageSize = 50;
        var result = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            Recursive = true,
            StartIndex = startIndex,
            Limit = pageSize + 1,
            IncludeItemTypes = ResolveAdminSearchKinds(kind, musicItems, seriesItems).ToArray(),
            IsVirtualItem = false,
            EnableTotalRecordCount = false,
            OrderBy = [ResolveAdminItemSort(sort)]
        });
        var supportedItems = result.Items
            .Where(item => !item.IsVirtualItem)
            .Select(item => new AdminItemDto(item.Id, AdminItemName(item), item.GetType().Name))
            .ToList();
        var hasMore = supportedItems.Count > pageSize;
        return new AdminItemBrowseResponse(supportedItems.Take(pageSize).ToList(), hasMore);
    }

    /// <summary>
    /// Copies progress once or creates an ongoing link between two users.
    /// </summary>
    /// <param name="request">The item, users, and optional ongoing sync mode.</param>
    /// <returns>The copy result and any active link.</returns>
    [HttpPost("Admin/Sync")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AdminSyncResponse> AdminSync([FromBody] AdminSyncRequest? request)
    {
        if (request is null)
        {
            return BadRequest("Sync settings are required.");
        }

        if (request.Mode is { } requestedMode && !Enum.IsDefined(requestedMode))
        {
            return BadRequest("Choose a valid sync mode.");
        }

        var source = GetEnabledUser(request.SourceUserId);
        var target = GetEnabledUser(request.TargetUserId);
        if (source is null || target is null || source.Id == target.Id)
        {
            return BadRequest("Select two different enabled users.");
        }

        var item = GetVisibleItem(request.ItemId, source);
        if (item is null || GetVisibleItem(request.ItemId, target) is null)
        {
            return NotFound();
        }

        if (!IsSupported(item))
        {
            return BadRequest("Choose a movie, series, or audio item.");
        }

        if (item is MusicAlbum && request.Mode is not null)
        {
            return BadRequest("Albums can be copied once; select songs for continuous sync.");
        }

        if (request.Mode is not { } mode)
        {
            return new AdminSyncResponse(
                _copier.CopyScope(source, target, item, UserDataSaveReason.UpdateUserData),
                null,
                null);
        }

        var rootId = item is Episode ? item.Id : LinkRules.ResolveRoot(item);
        if (rootId is not { } linkRootId)
        {
            return BadRequest("This item cannot be linked for continuous sync.");
        }

        var rootItem = GetVisibleItem(linkRootId, target);
        if (rootItem is null || GetVisibleItem(linkRootId, source) is null)
        {
            return NotFound();
        }

        var link = _store.Upsert(target.Id, source.Id, linkRootId, mode, BuildStaleCheck());
        _logger.LogInformation(
            "Progress Sync: administrator configured {Mode} sync from {SourceUser} to {TargetUser} on {ItemName} ({RootId})",
            mode,
            source.Username,
            target.Username,
            rootItem.Name,
            linkRootId);

        CopyResult? copy = null;
        string? copyError = null;
        try
        {
            copy = _copier.CopyScope(source, target, rootItem, UserDataSaveReason.UpdateUserData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: initial copy for admin-configured link {LinkId} failed", link.Id);
            copyError = ex.Message;
        }

        var adminLink = new AdminLinkDto(
            link.Id,
            target.Username,
            source.Username,
            AdminItemName(rootItem),
            linkRootId,
            link.Mode,
            link.CreatedUtc);
        return new AdminSyncResponse(copy, adminLink, copyError);
    }

    /// <summary>
    /// Deactivates a continuous sync link.
    /// </summary>
    /// <param name="linkId">The link id.</param>
    /// <returns>No content when the link is removed.</returns>
    [HttpDelete("Admin/Links/{linkId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteAdminLink([FromRoute] Guid linkId)
    {
        var link = _store.Snapshot.Find(linkId);
        if (link is null)
        {
            return NotFound();
        }

        _store.Remove(linkId, BuildStaleCheck());
        _logger.LogInformation(
            "Progress Sync: administrator deactivated {Mode} link {LinkId} on {RootId}",
            link.Mode,
            link.Id,
            link.RootItemId);
        return NoContent();
    }

    /// <summary>
    /// Gets the global sync settings and enabled user options for the admin page.
    /// </summary>
    /// <returns>Current settings and enabled users.</returns>
    [HttpGet("Admin/GlobalSettings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<GlobalSyncSettingsResponse> GetGlobalSyncSettings()
    {
        var users = _userManager.GetUsers()
            .Where(user => !user.HasPermission(PermissionKind.IsDisabled))
            .Select(user => new AdminUserDto(user.Id, user.Username))
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GlobalSyncSettingsResponse(_globalSettings.Current, users);
    }

    /// <summary>
    /// Adds a global sync profile for one pair of users.
    /// </summary>
    /// <param name="profile">The pair, sync direction, and media scopes.</param>
    /// <returns>The created profile.</returns>
    [HttpPost("Admin/GlobalProfiles")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<GlobalSyncProfile> CreateGlobalSyncProfile([FromBody] GlobalSyncProfile? profile)
    {
        if (!IsValidGlobalProfile(profile))
        {
            return BadRequest("Select two different enabled users, a valid sync mode, and at least one media type.");
        }

        var created = profile! with { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow };
        _globalSettings.AddProfile(created);
        _logger.LogInformation(
            "Progress Sync: administrator added global {Mode} profile {ProfileId} from {SourceUserId} to {TargetUserId}",
            created.Mode,
            created.Id,
            created.SourceUserId,
            created.TargetUserId);
        return created;
    }

    /// <summary>
    /// Removes a global sync profile.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <returns>No content when the profile is removed.</returns>
    [HttpDelete("Admin/GlobalProfiles/{profileId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteGlobalSyncProfile([FromRoute] Guid profileId)
    {
        var profile = _globalSettings.RemoveProfile(profileId);
        if (profile is null)
        {
            return NotFound();
        }

        _logger.LogInformation(
            "Progress Sync: administrator deactivated global {Mode} profile {ProfileId}",
            profile.Mode,
            profile.Id);
        return NoContent();
    }

    /// <summary>
    /// Replaces all global sync profiles.
    /// </summary>
    /// <param name="settings">The global sync profiles.</param>
    /// <returns>No content when saved.</returns>
    [HttpPost("Admin/GlobalSettings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult UpdateGlobalSyncSettings([FromBody] GlobalSyncSettings? settings)
    {
        if (settings is null)
        {
            return BadRequest("Settings are required.");
        }

        if (settings.Profiles is null
            || settings.Profiles.Any(profile => profile.Id == Guid.Empty || !IsValidGlobalProfile(profile))
            || settings.Profiles.Select(profile => profile.Id).Distinct().Count() != settings.Profiles.Count)
        {
            return BadRequest("Every profile must have a unique ID, two different enabled users, a valid sync mode, and at least one media type.");
        }

        _globalSettings.Save(settings);
        return NoContent();
    }

    /// <summary>
    /// Gets per-user concurrent stream limits.
    /// </summary>
    /// <returns>Enabled users and configured stream limits and stop policies.</returns>
    [HttpGet("Admin/StreamLimits")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ConcurrentStreamLimitsResponse> GetStreamLimits()
    {
        var users = GetEnabledAdminUsers();
        return new ConcurrentStreamLimitsResponse(
            users,
            _streamLimits.Limits
                .Select(pair => new ConcurrentStreamLimit(pair.Key, pair.Value.MaxConcurrentStreams, pair.Value.StopOldest))
                .OrderBy(limit => users.FirstOrDefault(user => user.Id == limit.UserId)?.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    /// <summary>
    /// Replaces per-user concurrent stream limits. A limit of zero disables enforcement but retains an oldest-stream preference.
    /// </summary>
    /// <param name="limits">Per-user maximum stream counts.</param>
    /// <returns>No content when saved.</returns>
    [HttpPost("Admin/StreamLimits")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult UpdateStreamLimits([FromBody] ConcurrentStreamLimit[]? limits)
    {
        if (limits is null
            || limits.Any(limit => limit.UserId == Guid.Empty || limit.MaxConcurrentStreams is < 0 or > 100)
            || limits.Select(limit => limit.UserId).Distinct().Count() != limits.Length)
        {
            return BadRequest("Provide unique enabled users and limits from 0 to 100.");
        }

        var enabledUserIds = GetEnabledAdminUsers().Select(user => user.Id).ToHashSet();
        if (limits.Any(limit => !enabledUserIds.Contains(limit.UserId)))
        {
            return BadRequest("Stream limits can only be set for enabled users.");
        }

        _streamLimits.Save(limits);
        _logger.LogInformation("Progress Sync: administrator updated concurrent stream limits for {UserCount} users", limits.Length);
        return NoContent();
    }

    /// <summary>
    /// Gets Pushover notification preferences without returning stored credentials.
    /// </summary>
    /// <returns>Current notification preferences and credential status.</returns>
    [HttpGet("Admin/Pushover")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PushoverNotificationsResponse> GetPushoverSettings()
    {
        var settings = _pushoverSettings.Current;
        return new PushoverNotificationsResponse(
            settings.IsConfigured,
            settings.NotifyAdded,
            settings.NotifyWatched,
            settings.NotifyDeleted,
            settings.NotifyPlaybackStarted,
            settings.NotifyPlaybackStopped);
    }

    /// <summary>
    /// Replaces Pushover event preferences and optionally updates the credentials.
    /// </summary>
    /// <param name="update">The settings to save.</param>
    /// <returns>No content when saved.</returns>
    [HttpPost("Admin/Pushover")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult UpdatePushoverSettings([FromBody] PushoverNotificationsUpdate? update)
    {
        if (update is null || update.AppToken?.Length > 128 || update.UserKey?.Length > 128)
        {
            return BadRequest("Pushover credentials must be no more than 128 characters.");
        }

        var current = _pushoverSettings.Current;
        var appToken = update.ClearCredentials
            ? null
            : string.IsNullOrWhiteSpace(update.AppToken) ? current.AppToken : update.AppToken.Trim();
        var userKey = update.ClearCredentials
            ? null
            : string.IsNullOrWhiteSpace(update.UserKey) ? current.UserKey : update.UserKey.Trim();
        var hasNotifications = update.NotifyAdded
            || update.NotifyWatched
            || update.NotifyDeleted
            || update.NotifyPlaybackStarted
            || update.NotifyPlaybackStopped;
        if (hasNotifications && (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey)))
        {
            return BadRequest("Configure both Pushover credentials before enabling notifications.");
        }

        _pushoverSettings.Save(update);
        _logger.LogInformation("Progress Sync: administrator updated Pushover notification settings");
        return NoContent();
    }

    /// <summary>Sends a test message using the configured Pushover credentials.</summary>
    /// <returns>No content on success.</returns>
    [HttpPost("Admin/Pushover/Test")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> TestPushover()
    {
        try
        {
            await _pushoverService.SendTestAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: Pushover test notification failed");
            return StatusCode(StatusCodes.Status502BadGateway, "Pushover could not deliver the test message. Check the credentials and server network.");
        }
    }

    private List<AdminUserDto> GetEnabledAdminUsers()
    {
        return _userManager.GetUsers()
            .Where(user => !user.HasPermission(PermissionKind.IsDisabled))
            .Select(user => new AdminUserDto(user.Id, user.Username))
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsValidGlobalProfile(GlobalSyncProfile? profile)
    {
        return profile is not null
            && profile.SourceUserId != Guid.Empty
            && profile.TargetUserId != Guid.Empty
            && profile.SourceUserId != profile.TargetUserId
            && GetEnabledUser(profile.SourceUserId) is not null
            && GetEnabledUser(profile.TargetUserId) is not null
            && Enum.IsDefined(profile.Mode)
            && (profile.Movies || profile.Series || profile.Music);
    }

    private static List<BaseItemKind> ResolveAdminSearchKinds(string? kind, string? musicItems, string? seriesItems)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode, BaseItemKind.Audio, BaseItemKind.MusicAlbum, BaseItemKind.MusicArtist];
        }

        return kind.Trim() switch
        {
            "movie" or "movies" => [BaseItemKind.Movie],
            "series" or "show" or "shows" => seriesItems?.Trim().ToLowerInvariant() is "episode" or "episodes"
                ? [BaseItemKind.Episode]
                : [BaseItemKind.Series],
            "music" or "audio" or "album" or "albums" => ResolveAdminMusicKinds(musicItems),
            _ => [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode, BaseItemKind.Audio, BaseItemKind.MusicAlbum, BaseItemKind.MusicArtist]
        };
    }

    private static string AdminItemName(BaseItem item)
    {
        if (item is Episode episode)
        {
            var name = FormatEpisodeName(episode, episode.SeriesName);
            return string.IsNullOrWhiteSpace(name) ? episode.Name : name;
        }

        return item.Name;
    }

    private static List<BaseItemKind> ResolveAdminMusicKinds(string? musicItems)
    {
        return musicItems?.Trim().ToLowerInvariant() switch
        {
            "song" or "songs" or "track" or "tracks" => [BaseItemKind.Audio],
            "both" or "all" => [BaseItemKind.Audio, BaseItemKind.MusicAlbum],
            _ => [BaseItemKind.MusicAlbum]
        };
    }

    private static (ItemSortBy SortBy, SortOrder SortOrder) ResolveAdminItemSort(string? sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "added" => (ItemSortBy.DateCreated, SortOrder.Descending),
            "oldest-added" => (ItemSortBy.DateCreated, SortOrder.Ascending),
            "released" => (ItemSortBy.PremiereDate, SortOrder.Descending),
            "oldest-released" => (ItemSortBy.PremiereDate, SortOrder.Ascending),
            "z-a" => (ItemSortBy.SortName, SortOrder.Descending),
            _ => (ItemSortBy.SortName, SortOrder.Ascending)
        };
    }

    private static bool IsSupported(BaseItem item) => item is Audio or Movie or Episode or Season or Series or MusicAlbum or MusicArtist;

    private User? GetCurrentUser()
    {
        // API keys authenticate with an empty user id; they are not users.
        var claim = User.FindFirst(UserIdClaimType)?.Value;
        return Guid.TryParse(claim, out var id) && !id.Equals(Guid.Empty) ? _userManager.GetUserById(id) : null;
    }

    private User? GetEnabledUser(Guid userId)
    {
        if (userId.Equals(Guid.Empty))
        {
            return null;
        }

        var user = _userManager.GetUserById(userId);
        return user is null || user.HasPermission(PermissionKind.IsDisabled) ? null : user;
    }

    private BaseItem? GetVisibleItem(Guid itemId, User user)
    {
        // GetItemById(id, user) checks library access and parental rules; BaseItem.IsVisible does not.
        return itemId.Equals(Guid.Empty) ? null : _libraryManager.GetItemById<BaseItem>(itemId, user);
    }

    private UserWatchStatusDto GetWatchStatus(User user, IReadOnlyList<BaseItem> versions)
    {
        var visibleVersions = versions
            .Select(version => GetVisibleItem(version.Id, user))
            .OfType<BaseItem>()
            .DistinctBy(version => version.Id)
            .ToList();
        if (visibleVersions.Count == 0)
        {
            return new UserWatchStatusDto(user.Id, user.Username, false, false, false, null);
        }

        var userData = _userDataManager.GetUserDataBatch(visibleVersions, user);
        var played = visibleVersions.Any(version => userData.GetValueOrDefault(version.Id)?.Played == true);
        var inProgress = visibleVersions.Any(version => (userData.GetValueOrDefault(version.Id)?.PlaybackPositionTicks ?? 0) > 0);
        int? progressPercent = visibleVersions
            .Select(version =>
            {
                var data = userData.GetValueOrDefault(version.Id);
                var runtimeTicks = version.RunTimeTicks;
                if (data is null || data.PlaybackPositionTicks <= 0 || runtimeTicks is not > 0)
                {
                    return null;
                }

                var percent = (double)data.PlaybackPositionTicks / runtimeTicks.Value * 100;
                return (int?)Math.Clamp(Math.Round(percent, MidpointRounding.AwayFromZero), 1, 99);
            })
            .Max();

        return new UserWatchStatusDto(user.Id, user.Username, true, played, inProgress, progressPercent);
    }

    private LinkDto? ToDto(SyncLink link, Guid me, bool isEpisodeOnly = false)
    {
        var isCreator = link.CreatorUserId.Equals(me);
        var partner = _userManager.GetUserById(isCreator ? link.PartnerUserId : link.CreatorUserId);
        return partner is null
            ? null
            : new LinkDto(link.Id, partner.Id, partner.Username, link.Mode, isCreator, LinkRules.CanRemove(link, me), link.CreatedUtc, isEpisodeOnly);
    }

    private Func<SyncLink, bool> BuildStaleCheck()
    {
        var userIds = _userManager.GetUsers().Select(u => u.Id).ToHashSet();
        return l => !userIds.Contains(l.CreatorUserId) || !userIds.Contains(l.PartnerUserId);
    }
}
