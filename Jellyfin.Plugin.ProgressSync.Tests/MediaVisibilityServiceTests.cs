using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class MediaVisibilityServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-media-visibility-service-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task ApplyAsync_AddsBlockedTagOnlyToTargetUserAndTagsCollectionChildren()
    {
        var target = CreateUser("target");
        var other = CreateUser("other");
        var targetPolicy = new UserPolicy { BlockedTags = ["existing-tag"] };
        var otherPolicy = new UserPolicy();
        var users = new Mock<IUserManager>();
        users.Setup(manager => manager.GetUsers()).Returns([target, other]);
        users.Setup(manager => manager.GetUserDto(target, string.Empty))
            .Returns(new UserDto { Policy = targetPolicy });
        users.Setup(manager => manager.GetUserDto(other, string.Empty))
            .Returns(new UserDto { Policy = otherPolicy });
        users.Setup(manager => manager.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
            .Returns(Task.CompletedTask);

        var album = new MusicAlbum { Id = Guid.NewGuid(), Name = "Seasonal album" };
        var track = new Audio { Id = Guid.NewGuid(), Name = "Seasonal song", ParentId = album.Id };
        var library = new Mock<ILibraryManager>();
        library.Setup(manager => manager.GetItemById<BaseItem>(album.Id)).Returns(album);
        library.Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) => query.ParentId == album.Id
                ? new QueryResult<BaseItem>([track])
                : new QueryResult<BaseItem>([]));
        library.Setup(manager => manager.UpdateItemAsync(
                It.IsAny<BaseItem>(),
                It.IsAny<BaseItem>(),
                ItemUpdateType.MetadataEdit,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var store = NewStore();
        var rule = new MediaVisibilityRule(
            Guid.NewGuid(),
            album.Id,
            target.Id,
            null,
            null,
            null,
            null,
            DateTime.UtcNow);
        store.Add(rule);
        var service = new MediaVisibilityService(
            library.Object,
            users.Object,
            store,
            NullLogger<MediaVisibilityService>.Instance);

        await service.ApplyAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(rule.VisibilityTag, album.Tags);
        Assert.Contains(rule.VisibilityTag, track.Tags);
        Assert.Contains(rule.VisibilityTag, targetPolicy.BlockedTags);
        Assert.Contains("existing-tag", targetPolicy.BlockedTags);
        Assert.DoesNotContain(rule.VisibilityTag, otherPolicy.BlockedTags);
        users.Verify(manager => manager.UpdatePolicyAsync(target.Id, targetPolicy), Times.Once);
        users.Verify(manager => manager.UpdatePolicyAsync(other.Id, otherPolicy), Times.Never);

        store.Remove(rule.Id);
        await service.ApplyAsync([album.Id], TestContext.Current.CancellationToken);

        Assert.DoesNotContain(rule.VisibilityTag, album.Tags);
        Assert.DoesNotContain(rule.VisibilityTag, track.Tags);
        Assert.Equal(["existing-tag"], targetPolicy.BlockedTags);
        users.Verify(manager => manager.UpdatePolicyAsync(target.Id, targetPolicy), Times.Exactly(2));
    }

    [Fact]
    public async Task ApplyAsync_AddsGlobalBlockedTagToEveryUser()
    {
        var first = CreateUser("first");
        var second = CreateUser("second");
        var firstPolicy = new UserPolicy();
        var secondPolicy = new UserPolicy();
        var users = new Mock<IUserManager>();
        users.Setup(manager => manager.GetUsers()).Returns([first, second]);
        users.Setup(manager => manager.GetUserDto(first, string.Empty)).Returns(new UserDto { Policy = firstPolicy });
        users.Setup(manager => manager.GetUserDto(second, string.Empty)).Returns(new UserDto { Policy = secondPolicy });
        users.Setup(manager => manager.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
            .Returns(Task.CompletedTask);

        var movie = new MediaBrowser.Controller.Entities.Movies.Movie { Id = Guid.NewGuid(), Name = "Global hide" };
        var library = new Mock<ILibraryManager>();
        library.Setup(manager => manager.GetItemById<BaseItem>(movie.Id)).Returns(movie);
        library.Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>([]));
        library.Setup(manager => manager.UpdateItemAsync(
                It.IsAny<BaseItem>(),
                It.IsAny<BaseItem>(),
                ItemUpdateType.MetadataEdit,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var store = NewStore();
        var rule = new MediaVisibilityRule(Guid.NewGuid(), movie.Id, null, null, null, null, null, DateTime.UtcNow);
        store.Add(rule);
        var service = new MediaVisibilityService(
            library.Object,
            users.Object,
            store,
            NullLogger<MediaVisibilityService>.Instance);

        await service.ApplyAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(rule.VisibilityTag, firstPolicy.BlockedTags);
        Assert.Contains(rule.VisibilityTag, secondPolicy.BlockedTags);
    }

    private static User CreateUser(string name)
    {
        return new User(name, "provider", "reset") { Id = Guid.NewGuid() };
    }

    private MediaVisibilityStore NewStore()
    {
        return new MediaVisibilityStore(
            Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"),
            NullLogger<MediaVisibilityStore>.Instance);
    }
}
