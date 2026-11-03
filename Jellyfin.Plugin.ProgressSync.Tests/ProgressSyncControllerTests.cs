using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Api;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

/// <summary>
/// The REST API is the security boundary: only the caller's own data is ever written.
/// </summary>
public sealed class ProgressSyncControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    private readonly LinkStore _store;
    private readonly GlobalSyncSettingsStore _globalSettings;
    private readonly ProgressCopier _copier;
    private readonly User _alice;
    private readonly User _bob;
    private readonly Series _series;
    private readonly Episode _episode;
    private readonly Movie _movie;
    private readonly Audio _audio;

    public ProgressSyncControllerTests()
    {
        _store = new LinkStore(Path.Combine(_dir, "links.json"), NullLogger<LinkStore>.Instance);
        _globalSettings = new GlobalSyncSettingsStore(Path.Combine(_dir, "global-settings.json"));
        _copier = new ProgressCopier(_server.Library.Object, _server.UserData.Object, NullLogger<ProgressCopier>.Instance);
        _alice = _server.AddUser("alice");
        _bob = _server.AddUser("bob");
        _series = _server.AddItem(new Series { Id = Guid.NewGuid(), Name = "Show" });
        _episode = _server.AddItem(new Episode { Id = Guid.NewGuid(), SeriesId = _series.Id });
        _movie = _server.AddItem(new Movie { Id = Guid.NewGuid(), Name = "Movie" });
        _audio = _server.AddItem(new Audio { Id = Guid.NewGuid(), Name = "Track" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private ProgressSyncController As(User? caller, string? rawClaim = null)
    {
        var controller = new ProgressSyncController(
            _server.Library.Object,
            _server.Users.Object,
            _server.UserData.Object,
            _copier,
            _store,
            _globalSettings,
            new WebInjectionStatus(),
            NullLogger<ProgressSyncController>.Instance);
        var claimValue = rawClaim ?? caller?.Id.ToString("N");
        Claim[] claims = claimValue is null ? [] : [new Claim("Jellyfin-UserId", claimValue)];
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static int? Status<T>(ActionResult<T> result) => (result.Result as IStatusCodeActionResult)?.StatusCode;

    private static int? Status(ActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;

    [Fact]
    public void CallersWithoutAUser_AreForbidden()
    {
        // No claim, and an API key (empty user id).
        foreach (var controller in new[] { As(null), As(null, Guid.Empty.ToString("N")) })
        {
            Assert.Equal(403, Status(controller.CopyFrom(_movie.Id, _alice.Id)));
            Assert.Equal(403, Status(controller.GetLinks(_movie.Id)));
            Assert.Equal(403, Status(controller.CreateLink(_movie.Id, new CreateLinkRequest(_alice.Id, LinkMode.Follow))));
            Assert.Equal(403, Status(controller.DeleteLink(Guid.NewGuid())));
        }
    }

    [Fact]
    public void CopyFrom_RejectsSelfUnknownAndInaccessible()
    {
        var bob = As(_bob);

        Assert.Equal(400, Status(bob.CopyFrom(_movie.Id, _bob.Id)));
        Assert.Equal(404, Status(bob.CopyFrom(_movie.Id, Guid.NewGuid())));
        Assert.Equal(404, Status(bob.CopyFrom(_movie.Id, Guid.Empty)));
        Assert.Equal(404, Status(bob.CopyFrom(Guid.Empty, _alice.Id)));

        _server.Hidden.Add((_bob.Id, _movie.Id));
        Assert.Equal(404, Status(bob.CopyFrom(_movie.Id, _alice.Id)));
        Assert.Empty(_server.Saves);
    }

    [Fact]
    public void CopyFrom_UnsupportedType_IsBadRequest()
    {
        var folder = _server.AddItem(new Folder { Id = Guid.NewGuid() });

        Assert.Equal(400, Status(As(_bob).CopyFrom(folder.Id, _alice.Id)));
    }

    [Fact]
    public void GetWatchStatus_RespectsItemAccessAndReportsWatchState()
    {
        var charlie = _server.AddUser("charlie");
        _movie.RunTimeTicks = 1000;
        _server.Data[(_alice.Id, _movie.Id)] = FakeServer.Progress(0, played: true);
        _server.Data[(_bob.Id, _movie.Id)] = FakeServer.Progress(500);
        _server.Hidden.Add((charlie.Id, _movie.Id));

        var result = As(_alice).GetWatchStatus(_movie.Id).Value!;

        Assert.Equal(3, result.Count);
        var aliceStatus = Assert.Single(result, status => status.UserId == _alice.Id);
        Assert.True(aliceStatus.CanAccess);
        Assert.True(aliceStatus.Played);
        Assert.False(aliceStatus.InProgress);
        Assert.Null(aliceStatus.ProgressPercent);
        var bobStatus = Assert.Single(result, status => status.UserId == _bob.Id);
        Assert.True(bobStatus.CanAccess);
        Assert.False(bobStatus.Played);
        Assert.True(bobStatus.InProgress);
        Assert.Equal(50, bobStatus.ProgressPercent);

        _episode.RunTimeTicks = 1000;
        _server.Data[(_bob.Id, _episode.Id)] = FakeServer.Progress(250);
        var episodeResult = As(_alice).GetWatchStatus(_episode.Id).Value!;
        var bobEpisodeStatus = Assert.Single(episodeResult, status => status.UserId == _bob.Id);
        Assert.True(bobEpisodeStatus.InProgress);
        Assert.Equal(25, bobEpisodeStatus.ProgressPercent);

        var charlieStatus = Assert.Single(result, status => status.UserId == charlie.Id);
        Assert.False(charlieStatus.CanAccess);
        Assert.False(charlieStatus.Played);
        Assert.False(charlieStatus.InProgress);
        Assert.Null(charlieStatus.ProgressPercent);
    }

    [Fact]
    public void GetWatchStatus_ReportsPartialProgressAndRejectsUnsupportedItems()
    {
        _movie.RunTimeTicks = 1000;
        _server.Data[(_bob.Id, _movie.Id)] = FakeServer.Progress(500);

        var result = As(_alice).GetWatchStatus(_movie.Id).Value!;

        var bobStatus = Assert.Single(result, status => status.UserId == _bob.Id);
        Assert.True(bobStatus.CanAccess);
        Assert.False(bobStatus.Played);
        Assert.True(bobStatus.InProgress);
        Assert.Equal(50, bobStatus.ProgressPercent);
        Assert.Equal(400, Status(As(_alice).GetWatchStatus(_audio.Id)));
        Assert.Equal(403, Status(As(null).GetWatchStatus(_movie.Id)));
    }

    [Fact]
    public void GetRecentlyWatchedUsers_ListsEnabledUsers()
    {
        var charlie = _server.AddUser("charlie");

        var result = As(_alice).GetRecentlyWatchedUsers().Value!;

        Assert.Equal(3, result.Count);
        Assert.Equal(_alice.Id, result[0].Id);
        Assert.Equal(_bob.Id, result[1].Id);
        Assert.Equal(charlie.Id, result[2].Id);
        Assert.Equal(403, Status(As(null).GetRecentlyWatchedUsers()));
    }

    [Fact]
    public void GetUserRecentlyWatched_ReturnsRecentAccessibleMoviesAndEpisodes()
    {
        var olderMovie = _server.AddItem(new Movie { Id = Guid.NewGuid(), Name = "Older" });
        var newerMovie = _server.AddItem(new Movie { Id = Guid.NewGuid(), Name = "Newer" });
        var episode = _server.AddItem(new Episode { Id = Guid.NewGuid(), Name = "Episode", SeriesId = _series.Id });
        var hiddenMovie = _server.AddItem(new Movie { Id = Guid.NewGuid(), Name = "Hidden" });
        var olderDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var newerDate = olderDate.AddDays(1);
        _server.RecentItems[_alice.Id] = [olderMovie, episode, hiddenMovie];
        _server.RecentItems[_bob.Id] = [newerMovie];
        _server.Data[(_alice.Id, olderMovie.Id)] = new UserItemData { Key = "key", Played = true, LastPlayedDate = olderDate };
        _server.Data[(_alice.Id, episode.Id)] = new UserItemData { Key = "key", Played = true, LastPlayedDate = newerDate };
        _server.Data[(_alice.Id, hiddenMovie.Id)] = new UserItemData { Key = "key", Played = true, LastPlayedDate = newerDate };
        _server.Data[(_bob.Id, newerMovie.Id)] = new UserItemData { Key = "key", Played = true, LastPlayedDate = newerDate };
        _server.Hidden.Add((_bob.Id, hiddenMovie.Id));

        var result = As(_bob).GetUserRecentlyWatched(_alice.Id).Value!;

        Assert.Equal(2, result.Count);
        Assert.Equal(episode.Id, result[0].ItemId);
        Assert.Equal("Episode", result[0].ItemType);
        Assert.Equal(newerDate, result[0].LastPlayedDate);
        Assert.Equal(olderMovie.Id, result[1].ItemId);
        Assert.Equal("Movie", result[1].ItemType);
        Assert.Equal(olderDate, result[1].LastPlayedDate);
        Assert.Equal(404, Status(As(_bob).GetUserRecentlyWatched(Guid.NewGuid())));
        Assert.Equal(403, Status(As(null).GetUserRecentlyWatched(_alice.Id)));
    }

    [Fact]
    public void GetRecentlyWatched_LimitsResultsToTwentyFive()
    {
        var movies = Enumerable.Range(0, 30)
            .Select(index => (BaseItem)_server.AddItem(new Movie { Id = Guid.NewGuid(), Name = "Movie " + index }))
            .ToList();
        var startDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _server.RecentItems[_alice.Id] = movies;
        for (var index = 0; index < movies.Count; index++)
        {
            _server.Data[(_alice.Id, movies[index].Id)] = new UserItemData
            {
                Key = "key",
                Played = true,
                LastPlayedDate = startDate.AddMinutes(index)
            };
        }

        var result = As(_alice).GetUserRecentlyWatched(_alice.Id).Value!;

        Assert.Equal(25, result.Count);
        Assert.Equal(movies[29].Id, result[0].ItemId);
        Assert.Equal(movies[5].Id, result[^1].ItemId);
    }

    [Fact]
    public void CreateLink_ValidatesModePartnerAndAccess()
    {
        var bob = As(_bob);

        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, null))));
        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, (LinkMode)42))));
        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(Guid.Empty, LinkMode.Follow))));
        Assert.Equal(400, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_bob.Id, LinkMode.Follow))));
        Assert.Equal(404, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(Guid.NewGuid(), LinkMode.Follow))));

        // The series must be visible to the partner as well.
        _server.Hidden.Add((_alice.Id, _series.Id));
        Assert.Equal(404, Status(bob.CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, LinkMode.Mirror))));
        Assert.Empty(_store.Snapshot.All);
    }

    [Fact]
    public void CreateLink_FromEpisode_BindsToSeries_WithCallerAsCreator()
    {
        var result = As(_bob).CreateLink(_episode.Id, new CreateLinkRequest(_alice.Id, LinkMode.Follow));

        Assert.NotNull(result.Value);
        var link = Assert.Single(_store.Snapshot.All);
        Assert.Equal(_bob.Id, link.CreatorUserId);
        Assert.Equal(_alice.Id, link.PartnerUserId);
        Assert.Equal(_series.Id, link.RootItemId);
        Assert.Equal(LinkMode.Follow, link.Mode);
    }

    [Fact]
    public void FollowLink_IsInvisibleAndNotRemovableForPartner()
    {
        var link = _store.Upsert(_bob.Id, _alice.Id, _series.Id, LinkMode.Follow, _ => false);

        Assert.Empty(As(_alice).GetLinks(_episode.Id).Value!);
        Assert.Equal(404, Status(As(_alice).DeleteLink(link.Id)));
        Assert.NotNull(_store.Snapshot.Find(link.Id));

        var bobsView = Assert.Single(As(_bob).GetLinks(_episode.Id).Value!);
        Assert.Equal(_alice.Id, bobsView.PartnerUserId);
        Assert.True(bobsView.CanRemove);
        Assert.Equal(204, Status(As(_bob).DeleteLink(link.Id)));
        Assert.Empty(_store.Snapshot.All);
    }

    [Fact]
    public void MirrorLink_IsVisibleAndRemovableForPartner()
    {
        var link = _store.Upsert(_bob.Id, _alice.Id, _series.Id, LinkMode.Mirror, _ => false);

        var alicesView = Assert.Single(As(_alice).GetLinks(_series.Id).Value!);
        Assert.Equal(_bob.Id, alicesView.PartnerUserId);
        Assert.False(alicesView.IsCreator);
        Assert.Equal(204, Status(As(_alice).DeleteLink(link.Id)));
        Assert.Empty(_store.Snapshot.All);
    }

    [Fact]
    public void AdminSync_OneTimeCopyCopiesSourceProgressWithoutCreatingALink()
    {
        _server.Data[(_alice.Id, _audio.Id)] = FakeServer.Progress(1200, played: true, playCount: 2);

        var result = As(_alice).AdminSync(new AdminSyncRequest(_audio.Id, _alice.Id, _bob.Id, null)).Value!;

        Assert.Null(result.Link);
        Assert.Null(result.CopyError);
        Assert.NotNull(result.Copy);
        Assert.Equal(1, result.Copy.Total);
        Assert.Equal(1, result.Copy.Updated);
        Assert.Equal(1200, _server.Get(_bob.Id, _audio.Id).PlaybackPositionTicks);
        Assert.Empty(_store.Snapshot.All);
    }

    [Theory]
    [InlineData(LinkMode.Follow)]
    [InlineData(LinkMode.Mirror)]
    public void AdminSync_ContinuousModesCreateActiveLinkAndCopySourceProgress(LinkMode mode)
    {
        _server.Data[(_alice.Id, _audio.Id)] = FakeServer.Progress(2400);

        var result = As(_alice).AdminSync(new AdminSyncRequest(_audio.Id, _alice.Id, _bob.Id, mode)).Value!;

        Assert.NotNull(result.Link);
        Assert.Equal(mode, result.Link.Mode);
        Assert.Equal(_bob.Username, result.Link.CreatorName);
        Assert.Equal(_alice.Username, result.Link.PartnerName);
        Assert.Null(result.CopyError);
        Assert.NotNull(result.Copy);
        Assert.Equal(2400, _server.Get(_bob.Id, _audio.Id).PlaybackPositionTicks);
        var link = Assert.Single(_store.Snapshot.All);
        Assert.Equal(_bob.Id, link.CreatorUserId);
        Assert.Equal(_alice.Id, link.PartnerUserId);
        Assert.Equal(mode, link.Mode);
    }

    [Fact]
    public void AdminSync_RejectsSameUserAndItemsNotVisibleToBothUsers()
    {
        var controller = As(_alice);

        Assert.Equal(400, Status(controller.AdminSync(
            new AdminSyncRequest(_audio.Id, _alice.Id, _alice.Id, null))));

        _server.Hidden.Add((_bob.Id, _audio.Id));
        Assert.Equal(404, Status(controller.AdminSync(
            new AdminSyncRequest(_audio.Id, _alice.Id, _bob.Id, LinkMode.Follow))));
        Assert.Empty(_store.Snapshot.All);
    }

    [Fact]
    public void AdminSync_ReportsInitialCopyFailureAndKeepsContinuousLinkActive()
    {
        _server.ThrowOnSaveFor.Add(_bob.Id);
        _server.Data[(_alice.Id, _audio.Id)] = FakeServer.Progress(400);

        var result = As(_alice).AdminSync(
            new AdminSyncRequest(_audio.Id, _alice.Id, _bob.Id, LinkMode.Follow)).Value!;

        Assert.Null(result.Copy);
        Assert.NotNull(result.CopyError);
        Assert.Contains("simulated failure", result.CopyError, StringComparison.Ordinal);
        Assert.NotNull(result.Link);
        Assert.Single(_store.Snapshot.All);
    }

    [Fact]
    public void DeleteAdminLink_DeactivatesAnyContinuousLink()
    {
        var link = _store.Upsert(_bob.Id, _alice.Id, _audio.Id, LinkMode.Follow, _ => false);

        Assert.Equal(204, Status(As(_alice).DeleteAdminLink(link.Id)));
        Assert.Empty(_store.Snapshot.All);
    }

    [Fact]
    public void SearchAdminItems_RequiresTwoCharactersAndReturnsSupportedItems()
    {
        var controller = As(_alice);
        Assert.Equal(400, Status(controller.SearchAdminItems(" ")));

        _server.Library
            .Setup(library => library.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>([_movie, _series, _audio]));

        var items = controller.SearchAdminItems("  Show  ").Value!;
        Assert.Equal(3, items.Count);
        Assert.Contains(items, item => item.Id == _movie.Id && item.Type == "Movie");
        Assert.Contains(items, item => item.Id == _series.Id && item.Type == "Series");
        Assert.Contains(items, item => item.Id == _audio.Id && item.Type == "Audio");
    }

    [Fact]
    public void GlobalProfiles_RequireValidPairAndPersistMultipleSyncModes()
    {
        var controller = As(_alice);

        Assert.Equal(400, Status(controller.CreateGlobalSyncProfile(new GlobalSyncProfile
        {
            SourceUserId = _alice.Id,
            TargetUserId = _alice.Id,
            Movies = true
        })));

        Assert.Equal(400, Status(controller.CreateGlobalSyncProfile(new GlobalSyncProfile
        {
            SourceUserId = _alice.Id,
            TargetUserId = _bob.Id,
            Mode = (LinkMode)42,
            Movies = true,
        })));

        var follow = controller.CreateGlobalSyncProfile(new GlobalSyncProfile
        {
            SourceUserId = _alice.Id,
            TargetUserId = _bob.Id,
            Mode = LinkMode.Follow,
            Movies = true
        }).Value!;
        var mirror = controller.CreateGlobalSyncProfile(new GlobalSyncProfile
        {
            SourceUserId = _alice.Id,
            TargetUserId = _bob.Id,
            Mode = LinkMode.Mirror,
            Series = true
        }).Value!;

        var settings = controller.GetGlobalSyncSettings().Value!;
        Assert.Equal(2, settings.Settings.Profiles.Count);
        Assert.True(settings.Settings.IsActive);
        Assert.Equal(2, settings.Users.Count);
        Assert.Equal(LinkMode.Follow, settings.Settings.Profiles[0].Mode);
        Assert.Contains(settings.Settings.Profiles, profile => profile.Id == follow.Id && profile.Movies);
        Assert.Contains(settings.Settings.Profiles, profile => profile.Id == mirror.Id && profile.Series);

        Assert.Equal(204, Status(controller.DeleteGlobalSyncProfile(follow.Id)));

        var remaining = controller.GetGlobalSyncSettings().Value!.Settings;
        Assert.Single(remaining.Profiles);
        Assert.True(remaining.IsActive);
        Assert.Equal(404, Status(controller.DeleteGlobalSyncProfile(follow.Id)));

        Assert.Equal(204, Status(controller.DeleteGlobalSyncProfile(mirror.Id)));
        Assert.False(controller.GetGlobalSyncSettings().Value!.Settings.IsActive);
    }
}
