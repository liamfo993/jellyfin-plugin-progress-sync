const HISTORY_VIEWS = [
    { key: 'currentlyWatching', label: 'Currently watching' },
    { key: 'movies', label: 'Movies', itemType: 'Movie', inProgress: false },
    { key: 'episodes', label: 'Episodes', itemType: 'Episode', inProgress: false },
    { key: 'inProgressMovies', label: 'In-progress movies', itemType: 'Movie', inProgress: true },
    { key: 'inProgressEpisodes', label: 'In-progress episodes', itemType: 'Episode', inProgress: true }
];
let dialogSequence = 0;

export default function openWatchHistory(apiClient, dialogHelper) {
    const compactLayout = window.matchMedia('(max-width: 80em), (max-height: 45em)').matches;
    const dialog = dialogHelper.createDialog({
        removeOnClose: true,
        size: compactLayout ? 'fullscreen' : 'small',
        scrollY: true
    });
    dialog.classList.add('formDialog', 'ps-watch-history-dialog');
    const dialogId = ++dialogSequence;
    dialog.innerHTML = ''
        + '<style>'
        + '.ps-watch-history-dialog{width:min(96vw,1100px);max-width:96vw}'
        + '@media(max-width:80em),(max-height:45em){.ps-watch-history-dialog{position:fixed!important;top:0!important;right:0!important;bottom:0!important;left:0!important;width:auto!important;height:auto!important;max-width:none!important;max-height:none!important;margin:0!important}}'
        + '.ps-watch-history-dialog [hidden]{display:none!important}'
        + '.ps-watch-history-dialog .formDialogContent{min-height:0}'
        + '.ps-wh-users{display:grid;gap:.75em;padding:1em}'
        + '.ps-wh-user{overflow:hidden;border:1px solid rgba(255,255,255,.14);border-radius:10px;background:rgba(255,255,255,.04)}'
        + '.ps-wh-user[open]{border-color:rgba(0,164,220,.5)}'
        + '.ps-wh-summary{display:flex;align-items:center;gap:.85em;padding:.85em 1em;cursor:pointer;list-style:none}'
        + '.ps-wh-summary::-webkit-details-marker{display:none}'
        + '.ps-wh-avatar{display:grid;width:2.5em;height:2.5em;flex:0 0 2.5em;place-items:center;border-radius:50%;background:rgba(0,164,220,.2);color:#00a4dc;font-weight:700}'
        + '.ps-wh-name{flex:1;font-weight:600}'
        + '.ps-wh-hint,.ps-wh-date,.ps-wh-progress-label{color:var(--secondary-text-color,#aaa);font-size:.9em}'
        + '.ps-wh-content{padding:0 1em 1.25em}'
        + '.ps-wh-tabs{display:flex;overflow-x:auto;gap:.5em;margin-bottom:1em;border-bottom:1px solid rgba(255,255,255,.14)}'
        + '.ps-wh-tab{flex:0 0 auto;padding:.7em 1em;border:0;border-bottom:3px solid transparent;background:transparent;color:var(--secondary-text-color,#aaa);cursor:pointer;font:inherit;white-space:nowrap}'
        + '.ps-wh-tab[aria-selected="true"]{border-bottom-color:#00a4dc;color:inherit;font-weight:700}'
        + '.ps-wh-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(145px,1fr));gap:1em}'
        + '@media(max-width:600px){.ps-wh-grid{grid-template-columns:repeat(auto-fill,minmax(90px,1fr));gap:.6em}.ps-wh-details{gap:.3em;padding:.55em}.ps-wh-title{font-size:.9em}.ps-wh-date,.ps-wh-progress-label{font-size:.78em}}'
        + '.ps-wh-card{min-width:0;overflow:hidden;border:1px solid rgba(255,255,255,.14);border-radius:9px;background:rgba(255,255,255,.04);color:inherit;text-decoration:none}'
        + '.ps-wh-poster{position:relative;display:grid;overflow:hidden;aspect-ratio:2/3;place-items:center;background:rgba(0,164,220,.12)}'
        + '.ps-wh-poster img{width:100%;height:100%;object-fit:cover}'
        + '.ps-wh-details{display:grid;gap:.45em;padding:.8em}'
        + '.ps-wh-title{display:-webkit-box;overflow:hidden;min-height:2.8em;-webkit-box-orient:vertical;-webkit-line-clamp:2;font-weight:600;line-height:1.4}'
        + '.ps-wh-empty{grid-column:1/-1;padding:1em;border:1px dashed rgba(255,255,255,.16);border-radius:8px;color:var(--secondary-text-color,#aaa);text-align:center}'
        + '.ps-wh-progress{display:grid;gap:.35em}'
        + '.ps-wh-progress-track{height:.4em;overflow:hidden;border-radius:999px;background:rgba(255,255,255,.16)}'
        + '.ps-wh-progress-fill{display:block;height:100%;border-radius:inherit;background:#00a4dc}'
        + '</style>'
        + '<div class="formDialogHeader">'
        + '<button is="paper-icon-button-light" class="btnCancel autoSize" title="Close"><span class="material-icons arrow_back" aria-hidden="true"></span></button>'
        + '<h3 class="formDialogHeaderTitle">Watch History</h3>'
        + '</div>'
        + '<div class="formDialogContent smoothScrollY">'
        + '<div class="dialogContentInner">'
        + '<p class="fieldDescription">Recent movies, episodes, and in-progress titles. Items you cannot access are omitted.</p>'
        + '<p class="ps-wh-status fieldDescription" role="status" aria-live="polite">Loading users…</p>'
        + '<div class="ps-wh-users"></div>'
        + '</div>'
        + '</div>';

    dialog.querySelector('.btnCancel').addEventListener('click', function () {
        dialogHelper.close(dialog);
    });
    dialogHelper.open(dialog);

    loadUsers().catch(function (error) {
        dialog.querySelector('.ps-wh-status').textContent = 'Could not load users (' + errorText(error) + ').';
        console.warn('[ProgressSync]', 'could not load Watch History users', error);
    });

    async function loadUsers() {
        const users = await apiClient.getJSON(apiClient.getUrl('ProgressSync/RecentlyWatched/Users'));
        const container = dialog.querySelector('.ps-wh-users');
        const status = dialog.querySelector('.ps-wh-status');
        if (users.length === 0) {
            status.textContent = 'No enabled users found.';
            return;
        }

        const fragment = document.createDocumentFragment();
        for (const user of users) {
            fragment.appendChild(createUserCard(user));
        }
        container.appendChild(fragment);
        status.textContent = '';
    }

    function createUserCard(user) {
        const card = document.createElement('details');
        card.className = 'ps-wh-user';

        const summary = document.createElement('summary');
        summary.className = 'ps-wh-summary';
        const avatar = document.createElement('span');
        avatar.className = 'ps-wh-avatar';
        avatar.textContent = user.Name.trim().charAt(0).toLocaleUpperCase() || '?';
        const name = document.createElement('span');
        name.className = 'ps-wh-name';
        name.textContent = user.Name;
        const hint = document.createElement('span');
        hint.className = 'ps-wh-hint';
        hint.textContent = 'Expand to view';
        summary.append(avatar, name, hint);

        const content = document.createElement('div');
        content.className = 'ps-wh-content';
        const message = document.createElement('p');
        message.className = 'ps-wh-empty';
        message.textContent = 'Expand to load recent activity.';
        const playbackStatus = document.createElement('p');
        playbackStatus.className = 'ps-wh-empty';
        playbackStatus.setAttribute('role', 'status');
        playbackStatus.hidden = true;
        const tabs = document.createElement('div');
        tabs.className = 'ps-wh-tabs';
        tabs.setAttribute('role', 'tablist');
        tabs.setAttribute('aria-label', user.Name + ' watch history');
        const views = HISTORY_VIEWS.map((view, index) => {
            const panelId = 'ps-wh-' + dialogId + '-' + view.key + '-' + user.Id.replace(/[^a-zA-Z0-9_-]/g, '');
            const tab = document.createElement('button');
            tab.type = 'button';
            tab.className = 'ps-wh-tab';
            tab.setAttribute('role', 'tab');
            tab.id = panelId + '-tab';
            tab.setAttribute('aria-controls', panelId);
            tab.hidden = index === 0;
            tab.setAttribute('aria-selected', String(index === 1));
            tab.tabIndex = index === 1 ? 0 : -1;
            tab.textContent = view.label;

            const grid = document.createElement('div');
            grid.className = 'ps-wh-grid';
            grid.id = panelId;
            grid.setAttribute('role', 'tabpanel');
            grid.hidden = index !== 1;
            grid.setAttribute('aria-labelledby', tab.id);
            tab.addEventListener('click', function () {
                card.dataset.tabChosen = 'true';
                showTab(view, views);
            });
            return { ...view, tab, grid };
        });
        tabs.append(...views.map(view => view.tab));
        tabs.hidden = true;
        content.append(message, tabs, ...views.map(view => view.grid), playbackStatus);
        card.append(summary, content);

        let timer;
        let generation = 0;
        let lastPlayback = '';
        card.addEventListener('toggle', function () {
            generation++;
            clearTimeout(timer);
            if (card.open) {
                views[0].tab.hidden = true;
                showTab(views[1], views);
                delete card.dataset.tabChosen;
                refreshCurrentlyWatching(generation);
            }
            if (card.open && card.dataset.loaded !== 'true' && card.dataset.loading !== 'true') {
                loadUserHistory(user, card, hint, message, tabs, views).catch(function (error) {
                    hint.textContent = 'Could not load history';
                    message.hidden = false;
                    message.textContent = 'Could not load this history (' + errorText(error) + ').';
                    console.warn('[ProgressSync]', 'could not load a user Watch History', error);
                    delete card.dataset.loading;
                });
            }
        });

        async function refreshCurrentlyWatching(requestGeneration) {
            try {
                const items = await apiClient.getJSON(apiClient.getUrl('ProgressSync/CurrentlyWatching/Users/' + encodeURIComponent(user.Id)));
                if (!card.isConnected || !card.open || requestGeneration !== generation) return;
                if (!Array.isArray(items)) throw new Error('Invalid current playback response');
                const current = views[0];
                const wasHidden = current.tab.hidden;
                current.tab.hidden = items.length === 0;
                if (items.length) {
                    const playbackKey = JSON.stringify(items.map(item => [
                        item.ItemId, item.ItemName, item.ItemType, item.SeriesId, item.ProgressPercent
                    ]));
                    if (playbackKey !== lastPlayback) {
                        populateGrid(current.grid, items, '');
                        lastPlayback = playbackKey;
                        import(apiClient.getUrl('ProgressSync/WatchHistoryRatingsScript'))
                            .then(module => module.default(apiClient, [current.grid]))
                            .catch(error => console.warn('[ProgressSync]', 'could not load Watch History ratings', error));
                    }
                    current.tab.textContent = current.label + ' (' + items.length + ')';
                    if (wasHidden && !card.dataset.tabChosen) showTab(current, views);
                } else {
                    lastPlayback = '';
                    if (current.tab.getAttribute('aria-selected') === 'true') showTab(views[1], views);
                }
                playbackStatus.hidden = true;
            } catch (error) {
                if (!card.isConnected || !card.open || requestGeneration !== generation) return;
                views[0].tab.hidden = true;
                if (views[0].tab.getAttribute('aria-selected') === 'true') showTab(views[1], views);
                playbackStatus.hidden = false;
                playbackStatus.textContent = 'Could not load current playback (' + errorText(error) + ').';
                console.warn('[ProgressSync]', 'could not load current playback', error);
            }
            if (card.isConnected && card.open && requestGeneration === generation) {
                timer = setTimeout(() => refreshCurrentlyWatching(requestGeneration), 10000);
            }
        }
        return card;
    }

    async function loadUserHistory(user, card, hint, message, tabs, views) {
        card.dataset.loading = 'true';
        message.hidden = true;
        tabs.hidden = false;
        showTab(views[1], views);
        hint.textContent = 'Loading…';
        for (const view of views.slice(1)) {
            populateGrid(view.grid, [], 'Loading…');
        }

        const userId = encodeURIComponent(user.Id);
        let totalItems = 0;
        let allLoaded = true;
        async function loadGroup(inProgress) {
            const groupViews = views.filter(view => view.inProgress === inProgress);
            try {
                const route = inProgress ? 'ProgressSync/InProgress/Users/' : 'ProgressSync/RecentlyWatched/Users/';
                const items = await apiClient.getJSON(apiClient.getUrl(route + userId));
                if (!card.isConnected) {
                    return;
                }
                if (!Array.isArray(items)) {
                    throw new Error('Invalid watch history response');
                }
                let groupCount = 0;
                for (const view of groupViews) {
                    const matchingItems = items.filter(item => item.ItemType === view.itemType);
                    groupCount += matchingItems.length;
                    populateGrid(view.grid, matchingItems, inProgress
                        ? 'No in-progress ' + view.itemType.toLowerCase() + 's.'
                        : 'No recent ' + view.itemType.toLowerCase() + 's.');
                    view.tab.textContent = view.label + ' (' + matchingItems.length + ')';
                }
                totalItems += groupCount;
                if (groupCount > 0) {
                    import(apiClient.getUrl('ProgressSync/WatchHistoryRatingsScript'))
                        .then(module => module.default(apiClient, groupViews.map(view => view.grid)))
                        .catch(error => console.warn('[ProgressSync]', 'could not load Watch History ratings', error));
                }
            } catch (error) {
                allLoaded = false;
                for (const view of groupViews) {
                    populateGrid(view.grid, [], 'Could not load this history (' + errorText(error) + ').');
                }
                console.warn('[ProgressSync]', 'could not load Watch History', error);
            }
        }

        await Promise.all([loadGroup(false), loadGroup(true)]);
        if (!card.isConnected) {
            return;
        }
        hint.textContent = allLoaded
            ? totalItems + ' recent and in-progress items'
            : 'Some history could not be loaded';
        if (allLoaded) {
            card.dataset.loaded = 'true';
        }
        delete card.dataset.loading;
    }

    function showTab(selectedView, views) {
        for (const view of views) {
            const selected = view.key === selectedView.key;
            view.tab.setAttribute('aria-selected', String(selected));
            view.tab.tabIndex = selected ? 0 : -1;
            view.grid.hidden = !selected;
        }
    }

    function populateGrid(grid, items, emptyMessage) {
        grid.replaceChildren();
        if (items.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'ps-wh-empty';
            empty.textContent = emptyMessage;
            grid.appendChild(empty);
            return;
        }

        const fragment = document.createDocumentFragment();
        for (const item of items) {
            fragment.appendChild(createHistoryCard(item));
        }
        grid.appendChild(fragment);
    }

    function createHistoryCard(item) {
        const card = document.createElement('a');
        card.className = 'ps-wh-card';
        card.dataset.ratingItemId = item.ItemId;
        card.href = '#/details?id=' + encodeURIComponent(item.ItemId);
        card.addEventListener('click', function () {
            dialogHelper.close(dialog);
        });

        const poster = document.createElement('div');
        poster.className = 'ps-wh-poster';
        const image = document.createElement('img');
        image.alt = '';
        image.loading = 'lazy';
        image.decoding = 'async';
        const primaryImageId = item.ItemType === 'Episode' && item.SeriesId ? item.SeriesId : item.ItemId;
        image.src = apiClient.getScaledImageUrl(primaryImageId, { type: 'Primary', maxWidth: 360, quality: 90 });
        image.addEventListener('error', function () {
            if (primaryImageId !== item.ItemId && image.dataset.fallback !== 'true') {
                image.dataset.fallback = 'true';
                image.src = apiClient.getScaledImageUrl(item.ItemId, { type: 'Primary', maxWidth: 360, quality: 90 });
            } else {
                image.remove();
                const placeholder = document.createElement('span');
                placeholder.textContent = item.ItemType === 'Movie' ? 'Movie artwork unavailable'
                    : item.ItemType === 'Episode' ? 'Episode artwork unavailable' : 'Artwork unavailable';
                poster.prepend(placeholder);
            }
        });
        poster.appendChild(image);

        const details = document.createElement('div');
        details.className = 'ps-wh-details';
        const title = document.createElement('span');
        title.className = 'ps-wh-title';
        title.textContent = item.ItemName;
        const date = document.createElement('time');
        date.className = 'ps-wh-date';
        date.dateTime = item.LastPlayedDate;
        date.textContent = item.CurrentlyWatching ? 'Currently playing' : formatDate(item.LastPlayedDate);
        details.append(title, date);

        if (item.ProgressPercent !== null && item.ProgressPercent !== undefined) {
            const progress = document.createElement('div');
            progress.className = 'ps-wh-progress';
            const track = document.createElement('div');
            track.className = 'ps-wh-progress-track';
            track.setAttribute('role', 'progressbar');
            track.setAttribute('aria-valuemin', '0');
            track.setAttribute('aria-valuemax', '100');
            track.setAttribute('aria-valuenow', String(item.ProgressPercent));
            const fill = document.createElement('span');
            fill.className = 'ps-wh-progress-fill';
            fill.style.width = item.ProgressPercent + '%';
            track.appendChild(fill);
            const label = document.createElement('span');
            label.className = 'ps-wh-progress-label';
            label.textContent = item.ProgressPercent + '% watched';
            progress.append(track, label);
            details.appendChild(progress);
        }

        card.append(poster, details);
        return card;
    }

    function formatDate(value) {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return 'Date unavailable';
        }
        return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
    }
}

function errorText(error) {
    return error && typeof error.status === 'number' ? 'HTTP ' + error.status : 'network error';
}
