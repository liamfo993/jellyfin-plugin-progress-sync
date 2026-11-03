using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
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
    private static readonly TimeSpan StopCommandRetryDelay = TimeSpan.FromSeconds(30);
    private readonly ISessionManager _sessionManager;
    private readonly ITranscodeManager _transcodeManager;
    private readonly ConcurrentStreamLimitStore _store;
    private readonly ILogger<ConcurrentStreamLimitService> _logger;
    private readonly object _enforcementLock = new();
    private readonly HashSet<string> _sessionsBeingStopped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastStopCommandUtc = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _playSessionIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentStreamLimitService"/> class.
    /// </summary>
    /// <param name="sessionManager">The Jellyfin session manager.</param>
    /// <param name="transcodeManager">The Jellyfin transcoding manager.</param>
    /// <param name="store">The configured per-user limits.</param>
    /// <param name="logger">The service logger.</param>
    public ConcurrentStreamLimitService(
        ISessionManager sessionManager,
        ITranscodeManager transcodeManager,
        ConcurrentStreamLimitStore store,
        ILogger<ConcurrentStreamLimitService> logger)
    {
        _sessionManager = sessionManager;
        _transcodeManager = transcodeManager;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionEnded += OnSessionEnded;
        _logger.LogInformation("Progress Sync: concurrent stream limit enforcement started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
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

        List<(SessionInfo Session, string? PlaySessionId)> sessionsToStop;
        int limit;
        bool stopOldest;
        lock (_enforcementLock)
        {
            if (!_store.Limits.TryGetValue(startedSession.UserId, out var settings)
                || settings.MaxConcurrentStreams <= 0)
            {
                return;
            }

            limit = settings.MaxConcurrentStreams;
            stopOldest = settings.StopOldest;
            var activeSessions = _sessionManager.Sessions
                .Where(session => session.UserId == startedSession.UserId
                    && session.NowPlayingItem is not null)
                .ToList();
            if (activeSessions.Count <= limit)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var pendingCount = activeSessions.Count(session =>
                _sessionsBeingStopped.Contains(session.Id)
                || IsStopCommandRecent(session.Id, now));
            var overage = activeSessions.Count - limit - pendingCount;
            if (overage <= 0)
            {
                return;
            }

            var orderedSessions = settings.StopOldest
                ? activeSessions.OrderBy(session => session.LastPlaybackCheckIn).ThenBy(session => session.LastActivityDate)
                : activeSessions.OrderByDescending(session => session.LastPlaybackCheckIn).ThenByDescending(session => session.LastActivityDate);
            sessionsToStop = orderedSessions
                .Where(session => !_sessionsBeingStopped.Contains(session.Id)
                    && !IsStopCommandRecent(session.Id, now))
                .Take(overage)
                .Select(session => (
                    Session: session,
                    PlaySessionId: _playSessionIds.GetValueOrDefault(session.Id)))
                .ToList();
            if (sessionsToStop.Count == 0)
            {
                return;
            }

            foreach (var (session, _) in sessionsToStop)
            {
                _sessionsBeingStopped.Add(session.Id);
                _lastStopCommandUtc[session.Id] = now;
            }
        }

        foreach (var (session, playSessionId) in sessionsToStop)
        {
            if (!string.IsNullOrWhiteSpace(playSessionId))
            {
                try
                {
                    await _transcodeManager.KillTranscodingJobs(
                        session.DeviceId,
                        playSessionId,
                        _ => true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Progress Sync: failed to terminate transcoding for session {SessionId}", session.Id);
                }
            }

            try
            {
                await _sessionManager.SendPlaystateCommand(
                    session.Id,
                    session.Id,
                    new PlaystateRequest { Command = PlaystateCommand.Stop },
                    CancellationToken.None).ConfigureAwait(false);

                _logger.LogWarning(
                    "Progress Sync: requested playback stop for {Age} session {SessionId} for user {UserId}; concurrent stream limit is {Limit}",
                    stopOldest ? "oldest" : "newest",
                    session.Id,
                    startedSession.UserId,
                    limit);
            }
            catch (Exception ex)
            {
                RemoveStopRequest(session.Id);
                _logger.LogError(ex, "Progress Sync: failed to stop over-limit playback for session {SessionId}", session.Id);
            }
            finally
            {
                RemoveInFlightStop(session.Id);
            }
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        TrackPlaySession(eventArgs);
        _ = EnforceFromEventAsync(eventArgs.Session);
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        TrackPlaySession(eventArgs);
        _ = EnforceFromEventAsync(eventArgs.Session);
    }

    private void TrackPlaySession(PlaybackProgressEventArgs eventArgs)
    {
        if (eventArgs.Session is null || string.IsNullOrWhiteSpace(eventArgs.PlaySessionId))
        {
            return;
        }

        lock (_enforcementLock)
        {
            _playSessionIds[eventArgs.Session.Id] = eventArgs.PlaySessionId;
        }
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
        if (!string.IsNullOrEmpty(eventArgs.Session?.Id))
        {
            lock (_enforcementLock)
            {
                _playSessionIds.Remove(eventArgs.Session.Id);
            }
        }
    }

    private void OnSessionEnded(object? sender, SessionEventArgs eventArgs)
    {
        var sessionId = eventArgs.SessionInfo?.Id;
        RemovePendingSession(sessionId);
        if (!string.IsNullOrEmpty(sessionId))
        {
            lock (_enforcementLock)
            {
                _playSessionIds.Remove(sessionId);
            }
        }
    }

    private void RemovePendingSession(string? sessionId)
    {
        RemoveStopRequest(sessionId);
    }

    private void RemoveStopRequest(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        lock (_enforcementLock)
        {
            _sessionsBeingStopped.Remove(sessionId);
            _lastStopCommandUtc.Remove(sessionId);
        }
    }

    private void RemoveInFlightStop(string sessionId)
    {
        lock (_enforcementLock)
        {
            _sessionsBeingStopped.Remove(sessionId);
        }
    }

    private bool IsStopCommandRecent(string sessionId, DateTime now)
    {
        return _lastStopCommandUtc.TryGetValue(sessionId, out var lastStopCommandUtc)
            && now - lastStopCommandUtc < StopCommandRetryDelay;
    }
}
