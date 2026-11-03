using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class ConcurrentStreamLimitServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-stream-service-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task EnforceAsync_StopsNewestPlaybackWhenUserExceedsLimit()
    {
        var userId = Guid.NewGuid();
        var existingSession = CreateSession(userId, "existing");
        var overLimitSession = CreateSession(userId, "over-limit");
        var startedSession = CreateSession(userId, "new");
        existingSession.LastPlaybackCheckIn = DateTime.UtcNow.AddMinutes(-1);
        overLimitSession.LastPlaybackCheckIn = DateTime.UtcNow.AddSeconds(-1);
        startedSession.LastPlaybackCheckIn = DateTime.UtcNow;
        var otherUserSession = CreateSession(Guid.NewGuid(), "other-user");
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.SetupGet(manager => manager.Sessions)
            .Returns([existingSession, overLimitSession, startedSession, otherUserSession]);
        sessionManager.Setup(manager => manager.SendPlaystateCommand(
                string.Empty,
                It.IsAny<string>(),
                It.Is<PlaystateRequest>(request => request.Command == PlaystateCommand.Stop),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var store = new ConcurrentStreamLimitStore(
            Path.Combine(_dir, "stream-limits.json"),
            NullLogger<ConcurrentStreamLimitStore>.Instance);
        store.Save([new ConcurrentStreamLimit(userId, 1)]);
        var service = new ConcurrentStreamLimitService(
            sessionManager.Object,
            store,
            NullLogger<ConcurrentStreamLimitService>.Instance);

        await service.EnforceAsync(existingSession);

        sessionManager.Verify(manager => manager.SendPlaystateCommand(
            string.Empty,
            startedSession.Id,
            It.Is<PlaystateRequest>(request => request.Command == PlaystateCommand.Stop),
            It.IsAny<CancellationToken>()), Times.Once);
        sessionManager.Verify(manager => manager.SendPlaystateCommand(
            string.Empty,
            overLimitSession.Id,
            It.Is<PlaystateRequest>(request => request.Command == PlaystateCommand.Stop),
            It.IsAny<CancellationToken>()), Times.Once);
        sessionManager.Verify(manager => manager.SendPlaystateCommand(
            string.Empty,
            existingSession.Id,
            It.IsAny<PlaystateRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
        sessionManager.Verify(manager => manager.SendPlaystateCommand(
            string.Empty,
            otherUserSession.Id,
            It.IsAny<PlaystateRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnforceAsync_DoesNotStopSessionUnderLimit()
    {
        var userId = Guid.NewGuid();
        var startedSession = CreateSession(userId, "new");
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.SetupGet(manager => manager.Sessions).Returns([startedSession]);
        var store = new ConcurrentStreamLimitStore(
            Path.Combine(_dir, "stream-limits.json"),
            NullLogger<ConcurrentStreamLimitStore>.Instance);
        store.Save([new ConcurrentStreamLimit(userId, 1)]);
        var service = new ConcurrentStreamLimitService(
            sessionManager.Object,
            store,
            NullLogger<ConcurrentStreamLimitService>.Instance);

        await service.EnforceAsync(startedSession);

        sessionManager.Verify(manager => manager.SendPlaystateCommand(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<PlaystateRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SessionInfo CreateSession(Guid userId, string sessionId)
    {
        return new SessionInfo(Mock.Of<ISessionManager>(), NullLogger.Instance)
        {
            Id = sessionId,
            UserId = userId,
            NowPlayingItem = new BaseItemDto { Id = Guid.NewGuid() },
            SessionControllers = []
        };
    }
}
