# Progress Sync for Jellyfin

Sync progress, browse watch history, and manage playback and media visibility on your Jellyfin server.

[![Release](https://img.shields.io/github/v/release/liamfo993/jellyfin-plugin-progress-sync)](https://github.com/liamfo993/jellyfin-plugin-progress-sync/releases)
[![Jellyfin](https://img.shields.io/badge/Jellyfin-12.1%2B-AA5CC3)](https://jellyfin.org)
[![License](https://img.shields.io/github/license/liamfo993/jellyfin-plugin-progress-sync)](LICENSE)

This fork of [Progress Sync](https://github.com/lirem/jellyfin-plugin-progress-sync) adds admin
tools for visibility schedules, playback limits, notifications, and library-wide sync alongside
the original per-title progress controls.

## Features

- **Progress sync**
  - Copy progress once, or sync it continuously in one or both directions.
  - Choose a movie, entire series, individual episode, album, or song. Albums can be copied once;
    songs can also be continuously synced.
  - Create multiple library-wide sync profiles for movies, series, and music.
  - View and deactivate active syncs.
- **Media visibility**
  - Hide selected media from one user or everyone.
  - Hide it permanently or show it only within a date range that repeats every year.
  - Select multiple items and review or remove current rules with poster thumbnails.
- **Browse and select media**
  - Browse posters or search by title in the Progress sync and Media visibility tabs.
  - View series as entire series or episodes, and music as albums, songs, or both.
  - Sort by date added, release date, or alphabetically; load more results as needed.
- **Watch history and status**
  - Browse recent completed and in-progress movies and episodes by user.
  - Optionally add a Watch History shortcut to every user's Jellyfin avatar menu.
  - Configure which users can see each other's watch history and title-page watch status.
  - See users' watched or in-progress status on movie and episode pages.
- **Playback limits**
  - Set a maximum number of concurrent streams per user; zero means unlimited.
  - Choose whether to stop the newest or oldest stream when the limit is exceeded.
- **Pushover notifications**
  - Send selected movie/episode additions, deletions, watched events, and playback start/stop events.
  - Test the notification credentials from the Notifications tab.

Only playback progress is copied: resume position, watched state, play count, and last played date.
Favorites, ratings, and audio/subtitle choices are left alone.

## Requirements

- Jellyfin 12.1 or newer
- The [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)
  plugin for the sync and watch-status buttons on title pages and the optional user-menu shortcut.
  Server-side sync and the plugin's Dashboard pages do not require it.

## Installation

1. In Jellyfin, open **Dashboard → Plugins → Manage Repositories** and add these two repositories
   (the name is up to you):

   **File Transformation** (skip if you already have it installed)

   ```
   https://www.iamparadox.dev/jellyfin/plugins/manifest.json
   ```

   **Progress Sync**

   ```
   https://raw.githubusercontent.com/liamfo993/jellyfin-plugin-progress-sync/main/manifest.json
   ```

2. Install **File Transformation** and **Progress Sync** from the catalog.
3. Restart Jellyfin (**Dashboard → Restart**), then reload the web client.

## Usage

Admins can open **Watch History** from **Dashboard → Plugins**. To add a Watch History shortcut to
all users' avatar menus in Jellyfin Web, open the Watch History Dashboard page, enable **Show Watch
History in the user menu**, and save. This shortcut uses the File Transformation plugin and is
available in the web client; users may need to refresh Jellyfin Web after the setting changes.
The **Watch history visibility** section lets admins choose which other users each person can see
in Watch History and the title-page **Watch status** dialog. Until custom rules are saved, all
enabled users can see one another; each user can always see their own history.
Admins can also open the Progress Sync plugin page from the Plugins menu.

### Progress sync

In the **Progress sync** tab, admins can browse posters or search for a title, select a source and
target user, and choose:

- **Copy once:** copy current progress without creating a continuous link.
- **One-way:** copy current progress, then keep the target updated from the source.
- **Two-way:** copy current progress, then sync future changes in both directions.

Browse by movie, series, or music; sort by recently/oldest added, recently/oldest released, or
alphabetically. Series defaults to **Entire series**; choose **Episodes** for an individual
episode. Music can be shown by album, song, or both. Albums support one-time copying; select a
song for a continuous link. Use **Load more** to browse additional results.

The **Active syncs** section lists and deactivates continuous links and library-wide profiles.
Admins can create multiple library-wide profiles with their own user pair, direction, and movie,
series, and music scopes. These profiles apply to future changes only; they do not copy existing
progress when created.

With File Transformation installed, users can also open a movie, series, season, or episode and
click the sync button next to the other actions.

**Copy once** replaces your progress with another user's:

| Page    | What is copied                    |
|---------|-----------------------------------|
| Movie   | the movie, including all versions |
| Episode | that episode                      |
| Season  | the season's episodes             |
| Series  | every episode                     |

**Keep in sync** from a title page links the movie or whole series with another user, even when
opened from an episode page. It first copies their current progress to you. Episode-only links
can be created from the admin **Progress sync** tab.

- **Follow:** their progress is copied to you as they watch. Only you see this link.
- **Two-way:** progress is copied both ways. You both see the link, and either of you can remove it.

Links pass changes along. If Ann follows Ben and Ben has a two-way link with Cleo, Cleo's progress
reaches Ben and then Ann.

Syncing runs on the server, so progress made in any Jellyfin app is synced. The title-page button
itself is only in the web client.

### Media visibility

On the **Media visibility** tab, admins can browse posters or search for media, check multiple
items, and choose a user or **Everyone**. The Series filter offers entire series or episodes;
the Music filter offers albums, songs, or both. Sorting options match the Progress sync tab.

Rules hide media permanently by default. To show seasonal content only during a specific period,
enable the annual date range and choose the first and last visible dates. For example, December 1
through December 31 shows Christmas content in December and hides it outside that window **every
year**, using the server's local date.

Rules for a series, season, or album also apply to its child media. Jellyfin's server-side
blocked-tag access checks enforce the rules across clients using its normal item-access APIs.
Current rules show posters and details and can be removed from the same tab.

### Watch history

Expand a user on **Watch History** to see up to 25 completed movies and 25 completed episodes,
plus separate in-progress movie and episode lists with up to 25 items each. In-progress cards show
a progress bar and percentage when runtime is available. Items you cannot access are omitted.
On movie and episode pages, the **Watch status** button shows enabled users' watched, unwatched,
or in-progress status.

### Playback limits

On **Playback limits**, set a maximum of 0–100 simultaneous streams per user; **0** means
unlimited. Choose whether the newest or oldest stream stops when the user exceeds their limit,
then save. The action is recorded in the Jellyfin log.

### Notifications

On **Notifications**, enter a Pushover application token and user or group key, select the events
to report, and save. Options include movie/episode additions and deletions, watched events, and
playback starts and stops. Use **Send test notification** to verify the credentials. Stored
credentials are not returned to the browser; leave fields blank to keep them.

## Privacy

Progress Sync is meant for households whose members trust each other:

- Users can see enabled users and their progress according to the administrator's Watch history
  visibility rules. Items hidden by library access or parental controls are listed as unavailable.
  People are not told when someone follows them.
- The watch history dashboard only includes movies and episodes the current user can access.
- When enabled, Pushover notifications send selected item titles, user names, and playback device
  names to Pushover. Pushover credentials are stored in the plugin's server-side configuration.
- With a two-way link, your partner's actions on that title, such as marking it unwatched, change
  your progress too. There is no undo.

Library access and parental controls still apply. Every copy and link change is written to the
server log.

## Known limitations

- The button relies on File Transformation modifying the web client, which Jellyfin does not
  officially support. A future web client update may break the button; existing links keep syncing.
- If two linked users watch the same title at the same time, their positions overwrite each other.
- A show split across two library folders syncs live only for the series the link was created on.

## Troubleshooting

- **No sync button:** open the **Progress sync** tab. If the web client integration
  is inactive, install or enable File Transformation and restart Jellyfin. Then reload the browser
  once with Ctrl+Shift+R (Cmd+Shift+R on macOS).
- **An action fails:** look for `Progress Sync` lines in the Jellyfin log and
  [open an issue](https://github.com/liamfo993/jellyfin-plugin-progress-sync/issues).

## Building

Requires the .NET 10 SDK and Node.js for the page tests.

```sh
dotnet build -c Release
dotnet test -c Release
node --test Jellyfin.Plugin.ProgressSync.Tests/ProgressSyncPage.test.cjs
```

Releases are packaged by the release workflow (see [docs/RELEASING.md](docs/RELEASING.md)).
Design notes are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## License

[GPL-3.0](LICENSE)
