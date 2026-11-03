using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class PushoverNotificationServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-pushover-service-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task StartAsync_SendsEnabledLibraryWatchAndPlaybackEvents()
    {
        var userId = Guid.NewGuid();
        var user = new User("alice", "provider", "reset") { Id = userId };
        var libraryManager = new Mock<ILibraryManager>();
        var userDataManager = new Mock<IUserDataManager>();
        var userManager = new Mock<IUserManager>();
        userManager.Setup(manager => manager.GetUserById(userId)).Returns(user);
        var sessionManager = new Mock<ISessionManager>();
        var store = new PushoverNotificationStore(
            Path.Combine(_dir, "pushover-notifications.json"),
            NullLogger<PushoverNotificationStore>.Instance);
        store.Save(new PushoverNotificationsUpdate(
            "application-token",
            "user-key",
            false,
            true,
            true,
            true,
            true,
            true));

        var messages = new ConcurrentQueue<Dictionary<string, string>>();
        var allSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(messages, allSent, expectedCount: 5);
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient("ProgressSyncPushover"))
            .Returns(httpClient);
        var service = new PushoverNotificationService(
            libraryManager.Object,
            userDataManager.Object,
            userManager.Object,
            sessionManager.Object,
            store,
            httpClientFactory.Object,
            NullLogger<PushoverNotificationService>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.StartAsync(cancellationToken);

        try
        {
            var movie = new Movie { Id = Guid.NewGuid(), Name = "Example Movie" };
            libraryManager.Raise(manager => manager.ItemAdded += null, libraryManager.Object, new ItemChangeEventArgs { Item = movie });
            libraryManager.Raise(manager => manager.ItemRemoved += null, libraryManager.Object, new ItemChangeEventArgs { Item = movie });
            userDataManager.Raise(manager => manager.UserDataSaved += null, userDataManager.Object, new UserDataSaveEventArgs
            {
                UserId = userId,
                Item = movie,
                UserData = new UserItemData { Key = "test", Played = true, PlayCount = 1 },
                SaveReason = UserDataSaveReason.TogglePlayed
            });
            var session = new SessionInfo(Mock.Of<ISessionManager>(), NullLogger.Instance)
            {
                Id = "session",
                UserId = userId,
                UserName = "alice",
                DeviceName = "Living room",
                NowPlayingItem = new BaseItemDto { Name = "Example Movie" },
                SessionControllers = []
            };
            sessionManager.Raise(manager => manager.PlaybackStart += null, sessionManager.Object, new PlaybackProgressEventArgs
            {
                Session = session,
                MediaInfo = new BaseItemDto { Name = "Example Movie" }
            });
            sessionManager.Raise(manager => manager.PlaybackStopped += null, sessionManager.Object, new PlaybackStopEventArgs
            {
                Session = session,
                MediaInfo = new BaseItemDto { Name = "Example Movie" }
            });

            await allSent.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Assert.Equal(5, messages.Count);
            Assert.All(messages, message =>
            {
                Assert.Equal("application-token", message["token"]);
                Assert.Equal("user-key", message["user"]);
            });
            Assert.Contains(messages, message => message["title"] == "Library item added");
            Assert.Contains(messages, message => message["title"] == "Library item deleted");
            Assert.Contains(messages, message => message["message"] == "alice watched Example Movie.");
            Assert.Contains(messages, message => message["title"] == "Playback started");
            Assert.Contains(messages, message => message["title"] == "Playback stopped");
        }
        finally
        {
            await service.StopAsync(cancellationToken);
            httpClient.Dispose();
        }
    }

    private sealed class RecordingHandler(
        ConcurrentQueue<Dictionary<string, string>> messages,
        TaskCompletionSource<bool> allSent,
        int expectedCount) : HttpMessageHandler
    {
        private int _sentCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var fields = content.Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                    pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')),
                    StringComparer.Ordinal);
            messages.Enqueue(fields);
            if (Interlocked.Increment(ref _sentCount) == expectedCount)
            {
                allSent.TrySetResult(true);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":1}")
            };
        }
    }
}
