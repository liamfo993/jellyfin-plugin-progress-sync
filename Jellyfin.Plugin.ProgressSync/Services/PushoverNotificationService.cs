using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Sends configured Jellyfin library, watch, and playback events to Pushover.
/// </summary>
public sealed class PushoverNotificationService : IHostedService
{
    private static readonly Uri MessagesUri = new("https://api.pushover.net/1/messages.json");
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly PushoverNotificationStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PushoverNotificationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PushoverNotificationService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="userDataManager">The Jellyfin user-data manager.</param>
    /// <param name="userManager">The Jellyfin user manager.</param>
    /// <param name="sessionManager">The Jellyfin session manager.</param>
    /// <param name="store">The persisted notification settings.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The service logger.</param>
    public PushoverNotificationService(
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IUserManager userManager,
        ISessionManager sessionManager,
        PushoverNotificationStore store,
        IHttpClientFactory httpClientFactory,
        ILogger<PushoverNotificationService> logger)
    {
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _sessionManager = sessionManager;
        _store = store;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _logger.LogInformation("Progress Sync: Pushover event notifications started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        return Task.CompletedTask;
    }

    /// <summary>Sends a test message using the configured Pushover credentials.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task SendTestAsync(CancellationToken cancellationToken)
    {
        return SendAsync("Progress Sync test", "Pushover notifications are configured correctly.", cancellationToken);
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs eventArgs)
    {
        if (_store.Current.NotifyAdded && IsSupportedItem(eventArgs.Item))
        {
            QueueNotification("Library item added", DescribeItem(eventArgs.Item));
        }
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs eventArgs)
    {
        if (_store.Current.NotifyDeleted && IsSupportedItem(eventArgs.Item))
        {
            QueueNotification("Library item deleted", DescribeItem(eventArgs.Item));
        }
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs eventArgs)
    {
        if (!_store.Current.NotifyWatched
            || eventArgs.UserData?.Played != true
            || eventArgs.SaveReason is not (UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed)
            || !IsSupportedItem(eventArgs.Item))
        {
            return;
        }

        var userName = _userManager.GetUserById(eventArgs.UserId)?.Username ?? "A user";
        QueueNotification("Item watched", $"{userName} watched {DescribeItem(eventArgs.Item)}.");
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        if (!_store.Current.NotifyPlaybackStarted || eventArgs.Session is null)
        {
            return;
        }

        QueueNotification("Playback started", DescribeSession(eventArgs.Session, eventArgs.MediaInfo?.Name, "started"));
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs eventArgs)
    {
        if (!_store.Current.NotifyPlaybackStopped || eventArgs.Session is null)
        {
            return;
        }

        QueueNotification("Playback stopped", DescribeSession(eventArgs.Session, eventArgs.MediaInfo?.Name, "stopped"));
    }

    private void QueueNotification(string title, string message)
    {
        _ = SendFromEventAsync(title, message);
    }

    private async Task SendFromEventAsync(string title, string message)
    {
        try
        {
            await SendAsync(title, message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: failed to send Pushover notification for {Title}", title);
        }
    }

    private async Task SendAsync(string title, string message, CancellationToken cancellationToken)
    {
        var settings = _store.Current;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Configure both a Pushover application token and user key before sending notifications.");
        }

        var fields = new Dictionary<string, string>
        {
            ["token"] = settings.AppToken!,
            ["user"] = settings.UserKey!,
            ["title"] = Truncate(title, 250),
            ["message"] = Truncate(message, 1024)
        };
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClientFactory.CreateClient("ProgressSyncPushover")
            .PostAsync(MessagesUri, content, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Pushover returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.Number
            || status.GetInt32() != 1)
        {
            throw new HttpRequestException("Pushover rejected the notification.", null, HttpStatusCode.BadRequest);
        }

        _logger.LogInformation("Progress Sync: sent Pushover notification {Title}", title);
    }

    private static bool IsSupportedItem(BaseItem? item)
    {
        return item is Movie or Episode;
    }

    private static string DescribeItem(BaseItem item)
    {
        if (item is Episode episode)
        {
            var number = episode.ParentIndexNumber is not null && episode.IndexNumber is not null
                ? $" S{episode.ParentIndexNumber:00}E{episode.IndexNumber:00}"
                : string.Empty;
            return string.IsNullOrWhiteSpace(episode.SeriesName)
                ? $"{item.Name}{number}"
                : $"{episode.SeriesName}{number} - {item.Name}";
        }

        return item.Name;
    }

    private static string DescribeSession(SessionInfo session, string? itemName, string action)
    {
        var userName = string.IsNullOrWhiteSpace(session.UserName) ? "A user" : session.UserName;
        var item = string.IsNullOrWhiteSpace(itemName) ? "an item" : itemName;
        var device = string.IsNullOrWhiteSpace(session.DeviceName) ? string.Empty : $" on {session.DeviceName}";
        return $"{userName} {action} {item}{device}.";
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
