function configurationPageUrl(name) {
    return 'configurationpage?name=' + encodeURIComponent(name);
}

function getTabs() {
    return [
        { href: configurationPageUrl('WatchHistory'), name: 'Watch history' },
        { href: configurationPageUrl('PlaybackLimits'), name: 'Playback limits' },
        { href: configurationPageUrl('Notifications'), name: 'Notifications' },
        { href: configurationPageUrl('ProgressSync'), name: 'Progress sync' },
        { href: configurationPageUrl('MediaVisibility'), name: 'Media visibility' }
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

    LibraryMenu.setTabs('WatchHistory', 0, getTabs);

    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 0, getTabs);
        loadUsers();
        loadMenuSettings();
        loadVisibilitySettings();
    });

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

        const userKey = user.Id.replace(/[^a-zA-Z0-9_-]/g, '');
        const tabs = document.createElement('div');
        tabs.className = 'ps-history-tabs';
        tabs.setAttribute('role', 'tablist');
        tabs.setAttribute('aria-label', user.Name + ' watch history');
        const historyViews = [
            { key: 'movies', label: 'Movies', itemType: 'Movie', inProgress: false },
            { key: 'episodes', label: 'Episodes', itemType: 'Episode', inProgress: false },
            { key: 'inProgressMovies', label: 'In-progress movies', itemType: 'Movie', inProgress: true },
            { key: 'inProgressEpisodes', label: 'In-progress episodes', itemType: 'Episode', inProgress: true }
        ].map((viewConfig, index) => {
            const panelId = 'ps-history-' + viewConfig.key + '-' + userKey;
            const tab = createHistoryTab(viewConfig.label, panelId, index === 0);
            const grid = createHistoryGrid(panelId);
            grid.hidden = true;
            return { ...viewConfig, tab, grid };
        });
        tabs.hidden = true;

        for (const viewConfig of historyViews) {
            viewConfig.tab.addEventListener('click', function () {
                showHistoryTab(viewConfig, historyViews);
            });
        }
        tabs.addEventListener('keydown', function (event) {
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
                return;
            }

            event.preventDefault();
            const currentIndex = historyViews.findIndex(viewConfig => viewConfig.tab === event.target);
            if (currentIndex < 0) {
                return;
            }

            const direction = event.key === 'ArrowRight' ? 1 : -1;
            const nextIndex = (currentIndex + direction + historyViews.length) % historyViews.length;
            const nextView = historyViews[nextIndex];
            showHistoryTab(nextView, historyViews);
            nextView.tab.focus();
        });

        tabs.append(...historyViews.map(viewConfig => viewConfig.tab));
        content.append(message, tabs, ...historyViews.map(viewConfig => viewConfig.grid));
        card.append(summary, content);
        card.addEventListener('toggle', function () {
            if (card.open && card.dataset.loaded !== 'true' && card.dataset.loading !== 'true') {
                loadUserHistory(user, card, hint, message, tabs, historyViews);
            }
        });

        return card;
    }

    async function loadUserHistory(user, card, hint, message, tabs, historyViews) {
        card.dataset.loading = 'true';
        message.hidden = true;
        tabs.hidden = false;
        showHistoryTab(historyViews[0], historyViews);
        hint.textContent = 'Loading…';
        for (const viewConfig of historyViews) {
            populateHistoryGrid(viewConfig.grid, [], 'Loading…');
        }

        try {
            const encodedUserId = encodeURIComponent(user.Id);
            const [watchedItems, inProgressItems] = await Promise.all([
                ApiClient.getJSON(ApiClient.getUrl('ProgressSync/RecentlyWatched/Users/' + encodedUserId))
                    .then(items => ({ items, error: null }), error => ({ items: null, error })),
                ApiClient.getJSON(ApiClient.getUrl('ProgressSync/InProgress/Users/' + encodedUserId))
                    .then(items => ({ items, error: null }), error => ({ items: null, error }))
            ]);
            if (!card.isConnected) {
                return;
            }

            const results = new Map([
                [false, watchedItems],
                [true, inProgressItems]
            ]);
            let totalItems = 0;
            let allLoaded = true;
            for (const viewConfig of historyViews) {
                const result = results.get(viewConfig.inProgress);
                if (result.error || !Array.isArray(result.items)) {
                    const error = result.error || new Error('Invalid watch history response');
                    allLoaded = false;
                    populateHistoryGrid(
                        viewConfig.grid,
                        [],
                        'Could not load this history (' + errorText(error) + ').');
                    console.warn('[ProgressSync]', 'could not load watch history', error);
                    viewConfig.tab.textContent = viewConfig.label;
                    continue;
                }

                const items = result.items.filter(item => item.ItemType === viewConfig.itemType);
                totalItems += items.length;
                populateHistoryGrid(
                    viewConfig.grid,
                    items,
                    viewConfig.inProgress
                        ? 'No in-progress ' + viewConfig.itemType.toLowerCase() + 's.'
                        : 'No recent ' + viewConfig.itemType.toLowerCase() + 's.');
                viewConfig.tab.textContent = viewConfig.label + ' (' + items.length + ')';
            }

            hint.textContent = totalItems + ' recent and in-progress items';
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
            showPosterPlaceholder(poster, item.ItemType);
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
    date.textContent = formatDate(item.LastPlayedDate);

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

function showPosterPlaceholder(poster, itemType) {
    const placeholder = document.createElement('span');
    placeholder.className = 'ps-history-placeholder';
    placeholder.textContent = itemType === 'Movie' ? 'Movie artwork unavailable' : 'Episode artwork unavailable';
    poster.replaceChildren(placeholder);
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
