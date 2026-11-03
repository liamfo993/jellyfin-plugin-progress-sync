// Progress Sync for Jellyfin: adds a "Sync progress" button to item detail pages.
// Loaded into jellyfin-web by the Progress Sync plugin through the File Transformation plugin.
(function () {
    'use strict';

    if (window.__progressSync) {
        return;
    }
    window.__progressSync = true;

    const LOG = '[ProgressSync]';
    const SUPPORTED = new Set(['Movie', 'Episode', 'Season', 'Series']);
    const homeObservers = new WeakMap();

    // jellyfin-web registers custom elements through the webcomponents v0 polyfill, which upgrades
    // elements created from markup. document.createElement(tag, { is }) throws under that polyfill.
    const BUTTON_HTML = '<button is="emby-button" type="button" class="button-flat btnProgressSync detailButton" title="Sync progress" aria-label="Sync progress">'
        + '<div class="detailButton-content"><span class="material-icons detailButton-icon sync" aria-hidden="true"></span></div></button>';

    const WATCH_STATUS_BUTTON_HTML = '<button is="emby-button" type="button" class="button-flat btnProgressSyncStatus detailButton" title="Watch status" aria-label="Watch status">'
        + '<div class="detailButton-content"><span class="material-icons detailButton-icon group" aria-hidden="true"></span></div></button>';

    const WATCH_STATUS_DIALOG_HTML = ''
        + '<div class="formDialogHeader">'
        + '<button is="paper-icon-button-light" class="btnCancel autoSize" tabindex="-1" title="Close"><span class="material-icons arrow_back" aria-hidden="true"></span></button>'
        + '<h3 class="formDialogHeaderTitle"></h3>'
        + '</div>'
        + '<div class="formDialogContent smoothScrollY">'
        + '<div class="dialogContentInner" style="padding-top:1em;">'
        + '<ul class="psWatchStatusList" style="list-style:none;padding:0;margin:0;"></ul>'
        + '<p class="psWatchStatusMessage" aria-live="polite">Loading…</p>'
        + '</div>'
        + '</div>';

    const DIALOG_HTML = ''
        + '<div class="formDialogHeader">'
        + '<button is="paper-icon-button-light" class="btnCancel autoSize" tabindex="-1" title="Close"><span class="material-icons arrow_back" aria-hidden="true"></span></button>'
        + '<h3 class="formDialogHeaderTitle"></h3>'
        + '</div>'
        + '<div class="formDialogContent smoothScrollY">'
        + '<div class="dialogContentInner dialog-content-centered" style="padding-top:1em;">'
        + '<h2 class="sectionTitle">Copy once</h2>'
        + '<div class="fieldDescription psCopyScope"></div>'
        + '<div class="selectContainer"><select is="emby-select" class="psCopyUser" label="Copy from"></select></div>'
        + '<button is="emby-button" type="button" class="raised button-submit block psCopy"><span>Copy to my account</span></button>'
        + '<h2 class="sectionTitle" style="margin-top:2em;">Keep in sync</h2>'
        + '<div class="fieldDescription psLinkScope"></div>'
        + '<div class="psLinks" style="margin:1em 0;"></div>'
        + '<div class="selectContainer"><select is="emby-select" class="psLinkUser" label="User"></select></div>'
        + '<div class="selectContainer"><select is="emby-select" class="psLinkMode" label="Mode">'
        + '<option value="Follow">Follow (their progress is copied to me)</option>'
        + '<option value="Mirror">Two-way</option>'
        + '</select></div>'
        + '<button is="emby-button" type="button" class="raised block psLink"><span>Link</span></button>'
        + '<p class="psStatus" aria-live="polite" style="min-height:1.5em;"></p>'
        + '</div>'
        + '</div>';

    const LINK_ROW_HTML = '<div class="psLinkRow" style="display:flex;align-items:center;gap:1em;margin:.5em 0;">'
        + '<span class="psLinkText" style="flex:1;"></span>'
        + '<button is="emby-button" type="button" class="raised psRemove"><span>Remove</span></button>'
        + '</div>';

    document.addEventListener('viewshow', onViewShow);

    // The script is deferred; handle a details view that is already visible (e.g. hard reload on #/details).
    queueMicrotask(function () {
        const home = document.querySelector('#indexPage:not(.hide)');
        if (home) {
            watchHomePage(home);
        }

        const view = document.querySelector('#itemDetailPage:not(.hide)');
        if (view) {
            handleView(view, idFromHash());
        }
    });

    function onViewShow(e) {
        const view = e.target;
        if (!(view instanceof HTMLElement)) {
            return;
        }

        if (view.id === 'indexPage') {
            watchHomePage(view);
            return;
        }

        if (view.id !== 'itemDetailPage') {
            return;
        }

        const params = e.detail && e.detail.params;
        handleView(view, (params && params.id) || idFromHash());
    }

    function idFromHash() {
        const query = window.location.hash.split('?')[1] || '';
        return new URLSearchParams(query).get('id');
    }

    function fromHtml(html) {
        const template = document.createElement('template');
        template.innerHTML = html;
        return template.content.firstElementChild;
    }

    function watchHomePage(view) {
        if (homeObservers.has(view)) {
            addRecentlyWatchedSection(view);
            return;
        }

        const sections = view.querySelector('#homeTab .sections');
        if (!sections) {
            return;
        }

        const observer = new MutationObserver(function () {
            addRecentlyWatchedSection(view);
        });
        observer.observe(sections, { childList: true, subtree: true });
        homeObservers.set(view, observer);
        addRecentlyWatchedSection(view);
    }

    function addRecentlyWatchedSection(view) {
        const container = view.querySelector('#homeTab .sections.homeSectionsContainer');
        if (!container || container.querySelector('.psRecentlyWatched')) {
            return;
        }

        const section = document.createElement('section');
        section.className = 'verticalSection psRecentlyWatched';
        section.style.cssText = 'padding:1em 0;';
        const title = document.createElement('h2');
        title.className = 'sectionTitle sectionTitle-cards padded-left';
        title.textContent = 'Recently watched';
        const list = document.createElement('ul');
        list.style.cssText = 'list-style:none;padding:0;margin:0;';
        const message = document.createElement('p');
        message.className = 'fieldDescription padded-left';
        message.textContent = 'Loading…';
        section.appendChild(title);
        section.appendChild(list);
        section.appendChild(message);
        container.appendChild(section);
        loadRecentlyWatched(section, list, message);
    }

    async function loadRecentlyWatched(section, list, message) {
        try {
            const apiClient = window.ApiClient;
            const entries = await apiClient.getJSON(apiClient.getUrl('ProgressSync/RecentlyWatched'));
            if (!section.isConnected) {
                return;
            }

            list.innerHTML = '';
            if (entries.length === 0) {
                message.textContent = 'No recently watched movies.';
                return;
            }

            for (const entry of entries) {
                const row = document.createElement('li');
                row.style.cssText = 'display:flex;flex-wrap:wrap;justify-content:space-between;gap:.5em 1em;padding:.6em 1em;border-bottom:1px solid rgba(255,255,255,.12);';
                const title = document.createElement('a');
                title.href = '#/details?id=' + encodeURIComponent(entry.ItemId);
                title.className = 'button-link';
                title.textContent = entry.ItemName;
                const details = document.createElement('span');
                details.textContent = 'Watched by ' + entry.UserName + ' · '
                    + new Date(entry.LastPlayedDate).toLocaleString();
                row.appendChild(title);
                row.appendChild(details);
                list.appendChild(row);
            }
            message.textContent = '';
        } catch (err) {
            if (section.isConnected) {
                message.textContent = 'Could not load recently watched (' + errorText(err) + ').';
            }
            console.warn(LOG, 'could not load recently watched', err);
        }
    }

    async function handleView(view, itemId) {
        try {
            const bar = view.querySelector('.mainDetailButtons');
            // A view never changes item id (jellyfin-web creates a new view per navigation),
            // so an existing button is always for this item.
            if (!itemId || !window.ApiClient || !bar || bar.querySelector('.btnProgressSync')) {
                return;
            }

            const apiClient = window.ApiClient;
            const item = await apiClient.getItem(apiClient.getCurrentUserId(), itemId);
            if (!item || !SUPPORTED.has(item.Type) || bar.querySelector('.btnProgressSync')) {
                return;
            }

            const button = fromHtml(BUTTON_HTML);
            let opening = false;
            button.addEventListener('click', function () {
                if (opening) {
                    return;
                }
                opening = true;
                openDialog(item).catch(function (err) {
                    console.warn(LOG, 'dialog failed', err);
                }).finally(function () {
                    opening = false;
                });
            });
            bar.insertBefore(button, bar.querySelector('.btnMoreCommands'));

            if (item.Type === 'Movie' || item.Type === 'Episode') {
                const statusButton = fromHtml(WATCH_STATUS_BUTTON_HTML);
                statusButton.addEventListener('click', function () {
                    openWatchStatus(item).catch(function (err) {
                        console.warn(LOG, 'watch status failed', err);
                    });
                });
                bar.insertBefore(statusButton, bar.querySelector('.btnMoreCommands'));
            }
        } catch (err) {
            console.warn(LOG, 'could not add button', err);
        }
    }

    function scopeLabels(type) {
        switch (type) {
            case 'Movie':
                return { copy: 'this movie', link: 'this movie' };
            case 'Episode':
                return { copy: 'this episode', link: 'the whole series' };
            case 'Season':
                return { copy: 'this season\'s episodes', link: 'the whole series' };
            default:
                return { copy: 'all episodes of this series', link: 'the whole series' };
        }
    }

    function confirmAsync(message, title) {
        // Dashboard.confirm is callback based and returns undefined.
        return new Promise(function (resolve) {
            window.Dashboard.confirm(message, title, resolve);
        });
    }

    function errorText(err) {
        return err && typeof err.status === 'number' ? 'HTTP ' + err.status : 'network error';
    }

    function fillUsers(select, users) {
        select.innerHTML = '';
        for (const user of users) {
            const option = document.createElement('option');
            option.value = user.Id;
            option.text = user.Name;
            select.appendChild(option);
        }
    }

    function selectedName(select) {
        const option = select.options[select.selectedIndex];
        return option ? option.text : '';
    }

    async function openWatchStatus(item) {
        const apiClient = window.ApiClient;
        const dialogHelper = window.Dashboard && window.Dashboard.dialogHelper;
        if (!dialogHelper) {
            console.warn(LOG, 'Dashboard.dialogHelper not available');
            return;
        }

        const dlg = dialogHelper.createDialog({ removeOnClose: true, size: 'small', scrollY: true });
        dlg.classList.add('formDialog');
        dlg.innerHTML = WATCH_STATUS_DIALOG_HTML;
        dlg.querySelector('.formDialogHeaderTitle').textContent = 'Watch status: ' + item.Name;
        dlg.querySelector('.btnCancel').addEventListener('click', function () {
            dialogHelper.close(dlg);
        });
        dialogHelper.open(dlg);

        const list = dlg.querySelector('.psWatchStatusList');
        const message = dlg.querySelector('.psWatchStatusMessage');
        try {
            const statuses = await apiClient.getJSON(
                apiClient.getUrl('ProgressSync/Items/' + item.Id + '/WatchStatus'));
            list.innerHTML = '';
            for (const status of statuses) {
                const row = document.createElement('li');
                row.style.cssText = 'display:flex;justify-content:space-between;gap:1em;padding:.6em 0;border-bottom:1px solid rgba(255,255,255,.12);';
                const name = document.createElement('span');
                name.textContent = status.UserName;
                const state = document.createElement('span');
                if (!status.CanAccess) {
                    state.textContent = 'Unavailable';
                } else if (status.Played) {
                    state.textContent = 'Watched';
                } else if (status.InProgress) {
                    state.textContent = typeof status.ProgressPercent !== 'number'
                        ? 'In progress'
                        : 'In progress (' + status.ProgressPercent + '%)';
                } else {
                    state.textContent = 'Unwatched';
                }
                row.appendChild(name);
                row.appendChild(state);
                list.appendChild(row);
            }
            message.textContent = statuses.length === 0 ? 'No enabled users found.' : '';
        } catch (err) {
            message.textContent = 'Could not load watch status (' + errorText(err) + ').';
            console.warn(LOG, 'could not load watch status', err);
        }
    }

    async function openDialog(item) {
        const apiClient = window.ApiClient;
        const dialogHelper = window.Dashboard && window.Dashboard.dialogHelper;
        if (!dialogHelper) {
            console.warn(LOG, 'Dashboard.dialogHelper not available');
            return;
        }

        const me = apiClient.getCurrentUserId();
        const users = (await apiClient.getUsers({ isDisabled: false }))
            .filter(function (u) { return u.Id !== me; })
            .sort(function (a, b) { return a.Name.localeCompare(b.Name); });
        const labels = scopeLabels(item.Type);

        const dlg = dialogHelper.createDialog({ removeOnClose: true, size: 'small', scrollY: false });
        dlg.classList.add('formDialog');
        dlg.innerHTML = DIALOG_HTML;

        const $ = function (selector) { return dlg.querySelector(selector); };
        const status = $('.psStatus');
        const copyUser = $('.psCopyUser');
        const linkUser = $('.psLinkUser');
        const linkMode = $('.psLinkMode');
        let busy = false;
        let changed = false;

        // The status line sits below the forms; keep it in view after each action (small screens).
        function setStatus(text) {
            status.textContent = text;
            if (text) {
                status.scrollIntoView({ block: 'nearest' });
            }
        }

        $('.formDialogHeaderTitle').textContent = 'Sync progress: ' + item.Name;
        $('.psCopyScope').textContent = 'Copy watch progress for ' + labels.copy
            + ' from another user to your account (resume position, watched state, play count, last played).';
        $('.psLinkScope').textContent = 'Keep ' + labels.link + ' in sync with another user. '
            + 'Follow copies their progress to you; two-way copies in both directions.';
        fillUsers(copyUser, users);
        fillUsers(linkUser, users);
        if (users.length === 0) {
            setStatus('No other users on this server.');
            $('.psCopy').hidden = true;
            $('.psLink').hidden = true;
        }

        // Buttons are never disabled (that would drop TV focus); a busy flag ignores repeat clicks.
        async function run(action) {
            if (busy) {
                return;
            }
            busy = true;
            try {
                await action();
            } catch (err) {
                setStatus('Failed: ' + errorText(err));
                console.warn(LOG, err);
            } finally {
                busy = false;
            }
        }

        async function loadLinks() {
            const links = await apiClient.getJSON(apiClient.getUrl('ProgressSync/Items/' + item.Id + '/Links'));
            const container = $('.psLinks');
            container.innerHTML = '';
            if (links.length === 0) {
                container.textContent = 'No links yet.';
                return;
            }

            for (const link of links) {
                const row = fromHtml(LINK_ROW_HTML);
                row.querySelector('.psLinkText').textContent = link.Mode === 'Mirror'
                    ? 'Two-way with ' + link.PartnerName
                    : 'Following ' + link.PartnerName;
                const remove = row.querySelector('.psRemove');
                if (link.CanRemove) {
                    remove.addEventListener('click', function () {
                        run(async function () {
                            setStatus('Working…');
                            await apiClient.ajax({ type: 'DELETE', url: apiClient.getUrl('ProgressSync/Links/' + link.Id) });
                            setStatus('Link removed.' + await refreshLinks());
                        });
                    });
                } else {
                    remove.remove();
                }
                container.appendChild(row);
            }
        }

        // Refreshes the link list after a change without turning a successful change into "Failed".
        async function refreshLinks() {
            try {
                await loadLinks();
                return '';
            } catch (err) {
                console.warn(LOG, 'could not refresh links', err);
                return ' (Could not refresh the list.)';
            }
        }

        $('.btnCancel').addEventListener('click', function () {
            dialogHelper.close(dlg);
        });

        $('.psCopy').addEventListener('click', function () {
            run(async function () {
                const userId = copyUser.value;
                if (!userId) {
                    return;
                }
                const confirmed = await confirmAsync(
                    'This replaces your watch progress for ' + labels.copy + ' with ' + selectedName(copyUser) + '\'s. Continue?',
                    'Copy progress');
                if (!confirmed) {
                    return;
                }
                setStatus('Working…');
                changed = true;
                const result = await apiClient.ajax({
                    type: 'POST',
                    url: apiClient.getUrl('ProgressSync/Items/' + item.Id + '/CopyFrom/' + userId),
                    dataType: 'json'
                });
                setStatus('Updated ' + result.Updated + ' of ' + result.Total + ' items.'
                    + (result.Skipped > 0 ? ' ' + result.Skipped + ' skipped (not available to one of you).' : ''));
            });
        });

        $('.psLink').addEventListener('click', function () {
            run(async function () {
                const userId = linkUser.value;
                if (!userId) {
                    return;
                }
                const confirmed = await confirmAsync(
                    'Your progress for ' + labels.link + ' will first be replaced with ' + selectedName(linkUser) + '\'s, then kept in sync. Continue?',
                    'Keep in sync');
                if (!confirmed) {
                    return;
                }
                setStatus('Working…');
                changed = true;
                const result = await apiClient.ajax({
                    type: 'POST',
                    url: apiClient.getUrl('ProgressSync/Items/' + item.Id + '/Links'),
                    data: JSON.stringify({ PartnerUserId: userId, Mode: linkMode.value }),
                    contentType: 'application/json',
                    dataType: 'json'
                });
                const copy = result.InitialCopy;
                const listNote = await refreshLinks();
                setStatus((copy
                    ? 'Linked. Updated ' + copy.Updated + ' of ' + copy.Total + ' items.'
                    : 'Linked, but copying the current progress failed: ' + result.InitialCopyError) + listNote);
            });
        });

        try {
            await loadLinks();
        } catch (err) {
            console.warn(LOG, 'could not load links', err);
            $('.psLinks').textContent = 'Could not load links (' + errorText(err) + ').';
        }

        // Core only refreshes the current item's play buttons on UserDataChanged, not season lists,
        // the series header or Next Up, so reload once after a change.
        dialogHelper.open(dlg).then(function () {
            if (changed) {
                window.location.reload();
            }
        });
    }
})();
