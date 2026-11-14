function configurationPageUrl(name) {
    return 'configurationpage?name=' + encodeURIComponent(name);
}

function getTabs() {
    return [
        { href: configurationPageUrl('WatchHistory'), name: 'Watch history' },
        { href: configurationPageUrl('PlaybackLimits'), name: 'Playback limits' },
        { href: configurationPageUrl('Notifications'), name: 'Notifications' },
        { href: configurationPageUrl('ProgressSync'), name: 'Progress sync' },
        { href: configurationPageUrl('MediaVisibility'), name: 'Media visibility' },
        { href: configurationPageUrl('Ratings'), name: 'Ratings' }
    ];
}

export default function (view) {
    const page = view.querySelector('#progressSyncWatchHistory');
    const usersContainer = page.querySelector('#psHistoryUsers');
    const status = page.querySelector('#psHistoryStatus');
    const userCount = page.querySelector('#psHistoryUserCount');
    const menuEnabled = page.querySelector('#psShowWatchHistoryMenu');
    const menuStatus = page.querySelector('#psWatchHistoryMenuStatus');
    const saveMenuButton = page.querySelector('#psSaveWatchHistoryMenu');
    const visibilityUsers = page.querySelector('#psVisibilityUsers');
    const visibilityStatus = page.querySelector('#psVisibilityStatus');
    const saveVisibilityButton = page.querySelector('#psSaveVisibility');
    let active = false;

    LibraryMenu.setTabs('WatchHistory', 0, getTabs);

    view.addEventListener('viewshow', function () {
        active = true;
        LibraryMenu.setTabs('WatchHistory', 0, getTabs);
        loadUsers();
        loadMenuSettings();
        loadVisibilitySettings();
    });
    view.addEventListener('viewhide', function () { active = false; });

    page.querySelector('#psRefreshHistory').addEventListener('click', loadUsers);
    saveMenuButton.addEventListener('click', saveMenuSettings);
    saveVisibilityButton.addEventListener('click', saveVisibilitySettings);

    async function loadMenuSettings() {
        menuStatus.textContent = 'Loading user-menu settings…';
        try {
            const settings = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/WatchHistoryMenu'));
            menuEnabled.checked = settings.Enabled;
            menuEnabled.disabled = false;
            saveMenuButton.disabled = false;
            menuStatus.textContent = '';
        } catch (error) {
            menuStatus.textContent = 'Could not load user-menu settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not load Watch History menu settings', error);
        }
    }

    async function saveMenuSettings() {
        saveMenuButton.disabled = true;
        menuStatus.textContent = 'Saving user-menu settings…';
        const settings = { Enabled: menuEnabled.checked };
        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/WatchHistoryMenu'),
                data: JSON.stringify(settings),
                contentType: 'application/json'
            });
            menuStatus.textContent = settings.Enabled
                ? 'Watch History is enabled in the user menu. Other users may need to refresh Jellyfin Web.'
                : 'Watch History is hidden from the user menu. Other users may need to refresh Jellyfin Web.';
            window.dispatchEvent(new CustomEvent('progressSync:watchHistoryMenuChanged', { detail: settings }));
        } catch (error) {
            menuStatus.textContent = 'Could not save user-menu settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not save Watch History menu settings', error);
        } finally {
            saveMenuButton.disabled = false;
            menuEnabled.disabled = false;
        }
    }

    async function loadVisibilitySettings() {
        visibilityStatus.textContent = 'Loading visibility settings…';
        saveVisibilityButton.disabled = true;
        try {
            const settings = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/Admin/WatchHistoryVisibility'));
            renderVisibilitySettings(settings);
            saveVisibilityButton.disabled = false;
            visibilityStatus.textContent = settings.IsConfigured
                ? 'Changes affect Watch History and the title-page Watch status button.'
                : 'No custom rules saved; all enabled users can see each other.';
        } catch (error) {
            visibilityStatus.textContent = 'Could not load visibility settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not load Watch History visibility settings', error);
        }
    }

    function renderVisibilitySettings(settings) {
        visibilityUsers.replaceChildren();
        const users = settings.Users || [];
        const configuredRules = new Map((settings.Rules || []).map(rule => [rule.UserId, new Set(rule.VisibleUserIds)]));
        const fragment = document.createDocumentFragment();

        for (const viewer of users) {
            const section = document.createElement('details');
            section.className = 'ps-visibility-user';
            section.dataset.viewerId = viewer.Id;
            section.dataset.viewerName = viewer.Name;

            const summary = document.createElement('summary');
            summary.textContent = viewer.Name + ' can see everyone';
            section.appendChild(summary);

            const note = document.createElement('div');
            note.className = 'fieldDescription';
            note.textContent = 'Their own history is always visible to them.';
            section.appendChild(note);

            const options = document.createElement('div');
            options.className = 'ps-visibility-options';
            const visibleIds = configuredRules.get(viewer.Id);
            let visibleCount = 0;
            for (const user of users) {
                if (user.Id === viewer.Id) {
                    continue;
                }
                const label = document.createElement('label');
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.dataset.viewerId = viewer.Id;
                checkbox.dataset.userId = user.Id;
                checkbox.checked = settings.IsConfigured
                    ? Boolean(visibleIds && visibleIds.has(user.Id))
                    : true;
                if (checkbox.checked) {
                    visibleCount++;
                }
                const text = document.createElement('span');
                text.textContent = user.Name;
                checkbox.addEventListener('change', updateSummary);
                label.append(checkbox, text);
                options.appendChild(label);
            }
            summary.textContent = viewer.Name + ' can see ' + visibleCount + ' other user' + (visibleCount === 1 ? '' : 's');
            section.appendChild(options);
            fragment.appendChild(section);
        }
        visibilityUsers.appendChild(fragment);
    }

    function updateSummary(event) {
        const section = event.currentTarget.closest('.ps-visibility-user');
        const selectedCount = section.querySelectorAll('input[type="checkbox"]:checked').length;
        section.querySelector('summary').textContent = section.dataset.viewerName + ' can see '
            + selectedCount + ' other user' + (selectedCount === 1 ? '' : 's');
    }

    async function saveVisibilitySettings() {
        saveVisibilityButton.disabled = true;
        visibilityStatus.textContent = 'Saving visibility settings…';
        const users = Array.from(visibilityUsers.querySelectorAll('.ps-visibility-user'));
        const settings = {
            IsConfigured: true,
            Rules: users.map(section => {
                const visibleUserIds = Array.from(section.querySelectorAll('input[type="checkbox"]:checked'))
                    .map(input => input.dataset.userId);
                const viewerId = section.dataset.viewerId;
                return { UserId: viewerId, VisibleUserIds: visibleUserIds };
            })
        };
        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/WatchHistoryVisibility'),
                data: JSON.stringify(settings),
                contentType: 'application/json'
            });
            visibilityStatus.textContent = 'Visibility settings saved.';
        } catch (error) {
            visibilityStatus.textContent = 'Could not save visibility settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not save Watch History visibility settings', error);
        } finally {
            saveVisibilityButton.disabled = false;
        }
    }

    async function loadUsers() {
        status.textContent = 'Loading users…';
        usersContainer.replaceChildren();
        try {
            const users = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/RecentlyWatched/Users'));
            userCount.textContent = String(users.length);
            if (users.length === 0) {
                status.textContent = 'No enabled users found.';
                return;
            }

            const fragment = document.createDocumentFragment();
            for (const user of users) {
                fragment.appendChild(createUserCard(user));
            }
            usersContainer.appendChild(fragment);
            status.textContent = '';
        } catch (error) {
            userCount.textContent = '—';
            status.textContent = 'Could not load users (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not load watch history users', error);
        }
    }

    function createUserCard(user) {
        const card = document.createElement('details');
        card.className = 'ps-user-card';

        const summary = document.createElement('summary');
        summary.className = 'ps-user-summary';

        const avatar = document.createElement('span');
        avatar.className = 'ps-user-avatar';
        avatar.textContent = user.Name.trim().charAt(0).toLocaleUpperCase() || '?';

        const name = document.createElement('span');
        name.className = 'ps-user-name';
        name.textContent = user.Name;

        const hint = document.createElement('span');
        hint.className = 'ps-user-hint';
        hint.textContent = 'Expand to view';

        summary.append(avatar, name, hint);

        const content = document.createElement('div');
        content.className = 'ps-history-content';

        const message = document.createElement('p');
        message.className = 'ps-history-empty';
        message.textContent = 'Expand to load recent activity.';
        const playbackStatus = document.createElement('p');
        playbackStatus.className = 'ps-history-empty';
        playbackStatus.setAttribute('role', 'status');
        playbackStatus.hidden = true;

        const userKey = user.Id.replace(/[^a-zA-Z0-9_-]/g, '');
        const tabs = document.createElement('div');
        tabs.className = 'ps-history-tabs';
        tabs.setAttribute('role', 'tablist');
        tabs.setAttribute('aria-label', user.Name + ' watch history');
        const historyViews = [
            { key: 'currentlyWatching', label: 'Currently watching' },
            { key: 'movies', label: 'Movies', itemType: 'Movie', inProgress: false },
            { key: 'episodes', label: 'Episodes', itemType: 'Episode', inProgress: false },
            { key: 'inProgressMovies', label: 'In-progress movies', itemType: 'Movie', inProgress: true },
            { key: 'inProgressEpisodes', label: 'In-progress episodes', itemType: 'Episode', inProgress: true }
        ].map((viewConfig, index) => {
            const panelId = 'ps-history-' + viewConfig.key + '-' + userKey;
            const tab = createHistoryTab(viewConfig.label, panelId, index === 1);
            tab.hidden = index === 0;
            const grid = createHistoryGrid(panelId);
            grid.hidden = true;
            return { ...viewConfig, tab, grid };
        });
        tabs.hidden = true;

        for (const viewConfig of historyViews) {
            viewConfig.tab.addEventListener('click', function () {
                card.dataset.tabChosen = 'true';
                showHistoryTab(viewConfig, historyViews);
            });
        }
        tabs.addEventListener('keydown', function (event) {
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
                return;
            }

            event.preventDefault();
            const visibleViews = historyViews.filter(viewConfig => !viewConfig.tab.hidden);
            const currentIndex = visibleViews.findIndex(viewConfig => viewConfig.tab === event.target);
            if (currentIndex < 0) {
                return;
            }

            const direction = event.key === 'ArrowRight' ? 1 : -1;
            const nextIndex = (currentIndex + direction + visibleViews.length) % visibleViews.length;
            const nextView = visibleViews[nextIndex];
            showHistoryTab(nextView, historyViews);
            nextView.tab.focus();
        });

        tabs.append(...historyViews.map(viewConfig => viewConfig.tab));
        content.append(message, tabs, ...historyViews.map(viewConfig => viewConfig.grid), playbackStatus);
        card.append(summary, content);
        let timer;
        let generation = 0;
        let lastPlayback = '';
        card.addEventListener('toggle', function () {
            generation++;
            clearTimeout(timer);
            if (card.open) {
                historyViews[0].tab.hidden = true;
                showHistoryTab(historyViews[1], historyViews);
                delete card.dataset.tabChosen;
                refreshCurrentlyWatching(generation);
            }
            if (card.open && card.dataset.loaded !== 'true' && card.dataset.loading !== 'true') {
                loadUserHistory(user, card, hint, message, tabs, historyViews);
            }
        });

        async function refreshCurrentlyWatching(requestGeneration) {
            try {
                const items = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/CurrentlyWatching/Users/' + encodeURIComponent(user.Id)));
                if (!active || !card.isConnected || !card.open || requestGeneration !== generation) return;
                if (!Array.isArray(items)) throw new Error('Invalid current playback response');
                const current = historyViews[0];
                const wasHidden = current.tab.hidden;
                current.tab.hidden = items.length === 0;
                if (items.length) {
                    const playbackKey = JSON.stringify(items.map(item => [
                        item.ItemId, item.ItemName, item.ItemType, item.SeriesId, item.ProgressPercent
                    ]));
                    if (playbackKey !== lastPlayback) {
                        populateHistoryGrid(current.grid, items, '');
                        lastPlayback = playbackKey;
                        import(ApiClient.getUrl('ProgressSync/WatchHistoryRatingsScript'))
                            .then(module => module.default(ApiClient, [current.grid]))
                            .catch(error => console.warn('[ProgressSync]', 'could not load Watch History ratings', error));
                    }
                    current.tab.textContent = current.label + ' (' + items.length + ')';
                    if (wasHidden && !card.dataset.tabChosen) showHistoryTab(current, historyViews);
                } else {
                    lastPlayback = '';
                    if (current.tab.getAttribute('aria-selected') === 'true') showHistoryTab(historyViews[1], historyViews);
                }
                playbackStatus.hidden = true;
            } catch (error) {
                if (!active || !card.isConnected || !card.open || requestGeneration !== generation) return;
                historyViews[0].tab.hidden = true;
                if (historyViews[0].tab.getAttribute('aria-selected') === 'true') showHistoryTab(historyViews[1], historyViews);
                playbackStatus.hidden = false;
                playbackStatus.textContent = 'Could not load current playback (' + errorText(error) + ').';
                console.warn('[ProgressSync]', 'could not load current playback', error);
            }
            if (active && card.isConnected && card.open && requestGeneration === generation) {
                timer = setTimeout(() => refreshCurrentlyWatching(requestGeneration), 10000);
            }
        }
        return card;
    }

    async function loadUserHistory(user, card, hint, message, tabs, historyViews) {
        card.dataset.loading = 'true';
        message.hidden = true;
        tabs.hidden = false;
        showHistoryTab(historyViews[1], historyViews);
        hint.textContent = 'Loading…';
        for (const viewConfig of historyViews.slice(1)) {
            populateHistoryGrid(viewConfig.grid, [], 'Loading…');
        }

        try {
            const encodedUserId = encodeURIComponent(user.Id);
            let totalItems = 0;
            let allLoaded = true;
            async function loadGroup(inProgress) {
                const groupViews = historyViews.filter(viewConfig => viewConfig.inProgress === inProgress);
                try {
                    const route = inProgress ? 'ProgressSync/InProgress/Users/' : 'ProgressSync/RecentlyWatched/Users/';
                    const items = await ApiClient.getJSON(ApiClient.getUrl(route + encodedUserId));
                    if (!card.isConnected) {
                        return;
                    }
                    if (!Array.isArray(items)) {
                        throw new Error('Invalid watch history response');
                    }
                    let groupCount = 0;
                    for (const viewConfig of groupViews) {
                        const matchingItems = items.filter(item => item.ItemType === viewConfig.itemType);
                        groupCount += matchingItems.length;
                        populateHistoryGrid(viewConfig.grid, matchingItems, inProgress
                            ? 'No in-progress ' + viewConfig.itemType.toLowerCase() + 's.'
                            : 'No recent ' + viewConfig.itemType.toLowerCase() + 's.');
                        viewConfig.tab.textContent = viewConfig.label + ' (' + matchingItems.length + ')';
                    }
                    totalItems += groupCount;
                    if (groupCount > 0) {
                        import(ApiClient.getUrl('ProgressSync/WatchHistoryRatingsScript'))
                            .then(module => module.default(ApiClient, groupViews.map(viewConfig => viewConfig.grid)))
                            .catch(error => console.warn('[ProgressSync]', 'could not load Watch History ratings', error));
                    }
                } catch (error) {
                    allLoaded = false;
                    for (const viewConfig of groupViews) {
                        populateHistoryGrid(viewConfig.grid, [], 'Could not load this history (' + errorText(error) + ').');
                        viewConfig.tab.textContent = viewConfig.label;
                    }
                    console.warn('[ProgressSync]', 'could not load watch history', error);
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
        } catch (error) {
            for (const viewConfig of historyViews) {
                populateHistoryGrid(
                    viewConfig.grid,
                    [],
                    'Could not load this history (' + errorText(error) + ').');
            }
            hint.textContent = 'Could not load history';
            console.warn('[ProgressSync]', 'could not load a user watch history', error);
        } finally {
            delete card.dataset.loading;
        }
    }
}

function createHistoryTab(label, panelId, selected) {
    const tab = document.createElement('button');
    tab.type = 'button';
    tab.className = 'ps-history-tab';
    tab.setAttribute('role', 'tab');
    tab.id = panelId + '-tab';
    tab.setAttribute('aria-controls', panelId);
    tab.setAttribute('aria-selected', String(selected));
    tab.tabIndex = selected ? 0 : -1;
    tab.textContent = label;
    return tab;
}

function createHistoryGrid(id) {
    const grid = document.createElement('div');
    grid.className = 'ps-history-grid';
    grid.id = id;
    grid.setAttribute('role', 'tabpanel');
    grid.setAttribute('aria-labelledby', id + '-tab');
    return grid;
}

function showHistoryTab(selectedView, historyViews) {
    for (const viewConfig of historyViews) {
        const isSelected = viewConfig.key === selectedView.key;
        viewConfig.tab.setAttribute('aria-selected', String(isSelected));
        viewConfig.tab.tabIndex = isSelected ? 0 : -1;
        viewConfig.grid.hidden = !isSelected;
    }
}

function populateHistoryGrid(grid, items, emptyMessage) {
    grid.replaceChildren();
    if (items.length === 0) {
        const empty = document.createElement('p');
        empty.className = 'ps-history-empty';
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
    card.className = 'ps-history-card';
    card.dataset.ratingItemId = item.ItemId;
    card.href = '#/details?id=' + encodeURIComponent(item.ItemId);

    const poster = document.createElement('div');
    poster.className = 'ps-history-poster';
    const image = document.createElement('img');
    image.alt = '';
    image.loading = 'lazy';
    image.decoding = 'async';
    const primaryImageId = item.ItemType === 'Episode' && item.SeriesId ? item.SeriesId : item.ItemId;
    image.src = ApiClient.getScaledImageUrl(primaryImageId, { type: 'Primary', maxWidth: 360, quality: 90 });
    image.addEventListener('error', function () {
        if (primaryImageId !== item.ItemId && image.dataset.fallback !== 'true') {
            image.dataset.fallback = 'true';
            image.src = ApiClient.getScaledImageUrl(item.ItemId, { type: 'Primary', maxWidth: 360, quality: 90 });
        } else {
            showPosterPlaceholder(image, item.ItemType);
        }
    });
    poster.appendChild(image);

    const details = document.createElement('div');
    details.className = 'ps-history-details';
    const title = document.createElement('span');
    title.className = 'ps-history-title';
    title.textContent = item.ItemName;

    const date = document.createElement('time');
    date.className = 'ps-history-date';
    date.dateTime = item.LastPlayedDate;
    date.textContent = item.CurrentlyWatching ? 'Currently playing' : formatDate(item.LastPlayedDate);

    details.append(title, date);
    if (item.ProgressPercent !== null && item.ProgressPercent !== undefined) {
        const progress = document.createElement('div');
        progress.className = 'ps-history-progress';
        progress.setAttribute('role', 'group');
        progress.setAttribute('aria-label', item.ProgressPercent + '% watched');

        const progressTrack = document.createElement('div');
        progressTrack.className = 'ps-history-progress-track';
        progressTrack.setAttribute('role', 'progressbar');
        progressTrack.setAttribute('aria-valuemin', '0');
        progressTrack.setAttribute('aria-valuemax', '100');
        progressTrack.setAttribute('aria-valuenow', String(item.ProgressPercent));

        const progressFill = document.createElement('span');
        progressFill.className = 'ps-history-progress-fill';
        progressFill.style.width = item.ProgressPercent + '%';
        progressTrack.appendChild(progressFill);

        const progressLabel = document.createElement('span');
        progressLabel.className = 'ps-history-progress-label';
        progressLabel.textContent = item.ProgressPercent + '% watched';
        progress.append(progressTrack, progressLabel);
        details.appendChild(progress);
    }

    card.append(poster, details);
    return card;
}

function showPosterPlaceholder(image, itemType) {
    const placeholder = document.createElement('span');
    placeholder.className = 'ps-history-placeholder';
    placeholder.textContent = itemType === 'Movie' ? 'Movie artwork unavailable'
        : itemType === 'Episode' ? 'Episode artwork unavailable' : 'Artwork unavailable';
    image.replaceWith(placeholder);
}

function formatDate(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return 'Date unavailable';
    }

    return new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short'
    }).format(date);
}

function errorText(error) {
    return error && typeof error.status === 'number' ? 'HTTP ' + error.status : 'network error';
}
