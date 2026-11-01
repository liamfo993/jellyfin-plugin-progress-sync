using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class LinkSyncServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    private readonly LinkStore _store;
    private readonly GlobalSyncSettingsStore _globalSettings;
    private readonly ProgressCopier _copier;
    private readonly User _alice;
    private readonly User _bob;
    private readonly User _carol;
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Episode _episode;

    public LinkSyncServiceTests()
    {
        _store = new LinkStore(Path.Combine(_dir, "links.json"), NullLogger<LinkStore>.Instance);
        _globalSettings = new GlobalSyncSettingsStore(Path.Combine(_dir, "global-settings.json"));
        _copier = new ProgressCopier(_server.Library.Object, _server.UserData.Object, NullLogger<ProgressCopier>.Instance);
        var service = new LinkSyncService(
            _server.UserData.Object,
            _server.Users.Object,
            _server.Library.Object,
            _store,
            _globalSettings,
            _copier,
            NullLogger<LinkSyncService>.Instance);
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _alice = _server.AddUser("alice");
        _bob = _server.AddUser("bob");
        _carol = _server.AddUser("carol");
        _episode = _server.AddItem(new Episode { Id = Guid.NewGuid(), SeriesId = _seriesId });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private void Link(User creator, User partner, LinkMode mode)
        => _store.Upsert(creator.Id, partner.Id, _seriesId, mode, _ => false);

    /// <summary>Simulates the server saving progress for a user (e.g. a playback progress report).</summary>
    private void Watch(User user, long ticks, UserDataSaveReason reason = UserDataSaveReason.PlaybackProgress, BaseItem? item = null)
        => _server.UserData.Object.SaveUserData(user, item ?? _episode, FakeServer.Progress(ticks), reason, CancellationToken.None);

    private long Ticks(User user) => _server.Get(user.Id, _episode.Id).PlaybackPositionTicks;

    private int SavesFor(User user) => _server.Saves.Count(s => s.User == user.Id);

    [Fact]
    public void Follow_CopiesPartnerToCreator_NotBack()
    {
        Link(_bob, _alice, LinkMode.Follow);

        Watch(_alice, 100);
        Assert.Equal(100, Ticks(_bob));
        Assert.Equal(UserDataSaveReason.PlaybackProgress, _server.Saves.Last().Reason);

        Watch(_bob, 200);
        Assert.Equal(100, Ticks(_alice));
    }

    [Fact]
    public void Mirror_CopiesBothWays()
    {
        Link(_bob, _alice, LinkMode.Mirror);

        Watch(_alice, 100);
        Assert.Equal(100, Ticks(_bob));

        Watch(_bob, 300);
        Assert.Equal(300, Ticks(_alice));
    }

    [Fact]
    public void Chain_IsTransitive_EachUserWrittenOnce_NoWriteBack()
    {
        Link(_alice, _bob, LinkMode.Mirror);
        Link(_bob, _carol, LinkMode.Mirror);

        Watch(_alice, 100);

        Assert.Equal(100, Ticks(_bob));
        Assert.Equal(100, Ticks(_carol));
        Assert.Equal(1, SavesFor(_alice));
        Assert.Equal(1, SavesFor(_bob));
        Assert.Equal(1, SavesFor(_carol));
    }

    [Theory]
    [InlineData(UserDataSaveReason.UpdateUserRating)]
    [InlineData(UserDataSaveReason.Import)]
    public void IgnoredReasons_DoNotPropagate(UserDataSaveReason reason)
    {
        Link(_bob, _alice, LinkMode.Mirror);

        Watch(_alice, 100, reason);

        Assert.Equal(0, SavesFor(_bob));
    }

    [Fact]
    public void NonEpisodeOrMovieItems_AreIgnored()
    {
        Link(_bob, _alice, LinkMode.Mirror);
        var season = _server.AddItem(new Season { Id = Guid.NewGuid(), SeriesId = _seriesId });

        Watch(_alice, 100, item: season);

        Assert.Equal(0, SavesFor(_bob));
    }

    [Fact]
    public void TargetWithoutAccess_IsSkipped()
    {
        Link(_bob, _alice, LinkMode.Mirror);
        _server.Hidden.Add((_bob.Id, _episode.Id));

        Watch(_alice, 100);

        Assert.Equal(0, SavesFor(_bob));
    }

    [Fact]
    public void FailingTarget_DoesNotStopOtherTargets()
    {
        Link(_alice, _bob, LinkMode.Mirror);
        Link(_alice, _carol, LinkMode.Mirror);
        _server.ThrowOnSaveFor.Add(_bob.Id);

        Watch(_alice, 100);

        Assert.Equal(100, Ticks(_carol));
    }

    [Fact]
    public void OneShotCopy_IntoLinkedUser_PropagatesToTheirPartners_NotToSource()
    {
        Link(_bob, _carol, LinkMode.Mirror);
        _server.Data[(_alice.Id, _episode.Id)] = FakeServer.Progress(700);

        _copier.CopyItems(_alice, _bob, new List<BaseItem> { _episode }, UserDataSaveReason.UpdateUserData);

        Assert.Equal(700, Ticks(_bob));
        Assert.Equal(700, Ticks(_carol));
        Assert.Equal(0, SavesFor(_alice));
        Assert.Null(PropagationContext.Current);
    }

    [Fact]
    public void GlobalMirror_SyncsSelectedMediaBothWays()
    {
        _globalSettings.Save(new GlobalSyncSettings
        {
            Profiles =
            [
                new GlobalSyncProfile
                {
                    Id = Guid.NewGuid(),
                    SourceUserId = _alice.Id,
                    TargetUserId = _bob.Id,
                    Mode = LinkMode.Mirror,
                    Movies = true,
                    Series = true,
                    Music = true
                }
            ]
        });
        var movie = _server.AddItem(new Movie { Id = Guid.NewGuid() });
        var audio = _server.AddItem(new Audio { Id = Guid.NewGuid() });

        Watch(_alice, 100, item: _episode);
        Watch(_bob, 200, item: movie);
        Watch(_alice, 300, item: audio);
        Watch(_carol, 400, item: audio);

        Assert.Equal(100, _server.Get(_bob.Id, _episode.Id).PlaybackPositionTicks);
        Assert.Equal(200, _server.Get(_alice.Id, movie.Id).PlaybackPositionTicks);
        Assert.Equal(300, _server.Get(_bob.Id, audio.Id).PlaybackPositionTicks);
        Assert.Equal(300, _server.Get(_alice.Id, audio.Id).PlaybackPositionTicks);
    }

    [Fact]
    public void GlobalProfiles_SupportMultiplePairsAndOneWayFlow()
    {
        var movie = _server.AddItem(new Movie { Id = Guid.NewGuid() });
        _globalSettings.Save(new GlobalSyncSettings
        {
            Profiles =
            [
                new GlobalSyncProfile
                {
                    Id = Guid.NewGuid(),
                    SourceUserId = _alice.Id,
                    TargetUserId = _bob.Id,
                    Mode = LinkMode.Follow,
                    Movies = true
                },
                new GlobalSyncProfile
                {
                    Id = Guid.NewGuid(),
                    SourceUserId = _bob.Id,
                    TargetUserId = _carol.Id,
                    Mode = LinkMode.Mirror,
                    Series = true
                }
            ]
        });

        Watch(_alice, 100, item: movie);
        Assert.Equal(100, _server.Get(_bob.Id, movie.Id).PlaybackPositionTicks);
        Watch(_bob, 200, item: movie);
        Assert.Equal(100, _server.Get(_alice.Id, movie.Id).PlaybackPositionTicks);

        Watch(_bob, 300);
        Assert.Equal(300, _server.Get(_carol.Id, _episode.Id).PlaybackPositionTicks);
        Watch(_carol, 400);
        Assert.Equal(400, _server.Get(_bob.Id, _episode.Id).PlaybackPositionTicks);
    }
}
