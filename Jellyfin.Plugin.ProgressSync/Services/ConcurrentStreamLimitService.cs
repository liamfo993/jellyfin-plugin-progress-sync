using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Stops a user's newly started playback when it exceeds their configured stream limit.
/// </summary>
public sealed class ConcurrentStreamLimitService : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly ConcurrentStreamLimitStore _store;
    private readonly ILogger<ConcurrentStreamLimitService> _logger;
    private readonly object _enforcementLock = new();
    private readonly HashSet<string> _sessionsBeingStopped = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentStreamLimitService"/> class.
    /// </summary>
    /// <param name="sessionManager">The Jellyfin session manager.</param>
    /// <param name="store">The configured per-user limits.</param>
    /// <param name="logger">The service logger.</param>
    public ConcurrentStreamLimitService(
        ISessionManager sessionManager,
        ConcurrentStreamLimitStore store,
        ILogger<ConcurrentStreamLimitService> logger)
    {
        _sessionManager = sessionManager;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionEnded += OnSessionEnded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionEnded -= OnSessionEnded;
        return Task.CompletedTask;
    }

    internal async Task EnforceAsync(SessionInfo? startedSession)
    {
        if (startedSession is null || startedSession.UserId == Guid.Empty)
        {
            return;
        }

        List<SessionInfo> sessionsToStop;
        int limit;
        lock (_enforcementLock)
        {
            if (!_store.Limits.TryGetValue(startedSession.UserId, out limit) || limit <= 0)
            {
                return;
            }

            var activeSessions = _sessionManager.Sessions
                .Where(session => session.UserId == startedSession.UserId
                    && session.IsActive
                    && session.NowPlayingItem is not null)
                .ToList();
            if (activeSessions.Count <= limit)
            {
                return;
            }

            var pendingCount = activeSessions.Count(session => _sessionsBeingStopped.Contains(session.Id));
            var overage = activeSessions.Count - limit - pendingCount;
            if (overage <= 0)
            {
                return;
            }

            sessionsToStop = activeSessions
                .OrderByDescending(session => session.LastPlaybackCheckIn)
                .ThenByDescending(session => session.LastActivityDate)
                .Where(session => !_sessionsBeingStopped.Contains(session.Id))
                .Take(overage)
                .ToList();
            if (sessionsToStop.Count == 0)
            {
                return;
            }

            foreach (var session in sessionsToStop)
            {
                _sessionsBeingStopped.Add(session.Id);
            }
        }

        foreach (var session in sessionsToStop)
        {
            try
            {
                await _sessionManager.SendPlaystateCommand(
                    string.Empty,
                    session.Id,
                    new PlaystateRequest { Command = PlaystateCommand.Stop },
                    CancellationToken.None).ConfigureAwait(false);

                _logger.LogWarning(
                    "Progress Sync: stopped newest playback session {SessionId} for user {UserId}; concurrent stream limit is {Limit}",
                    session.Id,
                    startedSession.UserId,
                    limit);
            }
            catch (Exception ex)
            {
                RemovePendingSession(session.Id);
                _logger.LogError(ex, "Progress Sync: failed to enforce concurrent stream limit for session {SessionId}", session.Id);
            }
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        _ = EnforceFromEventAsync(eventArgs.Session);
    }

    private async Task EnforceFromEventAsync(SessionInfo? session)
    {
        try
        {
            await EnforceAsync(session).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: failed to evaluate concurrent stream limit for user {UserId}", session?.UserId);
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs eventArgs)
    {
        RemovePendingSession(eventArgs.Session?.Id);
    }

    private void OnSessionEnded(object? sender, SessionEventArgs eventArgs)
    {
        RemovePendingSession(eventArgs.SessionInfo?.Id);
    }

    private void RemovePendingSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        lock (_enforcementLock)
        {
            _sessionsBeingStopped.Remove(sessionId);
        }
    }
}
