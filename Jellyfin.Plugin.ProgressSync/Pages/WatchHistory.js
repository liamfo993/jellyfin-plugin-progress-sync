function configurationPageUrl(name) {
    return 'configurationpage?name=' + encodeURIComponent(name);
}

function getTabs() {
    return [
        { href: configurationPageUrl('WatchHistory'), name: 'Dashboard' },
        { href: configurationPageUrl('ProgressSync'), name: 'Settings' }
    ];
}

export default function (view) {
    const page = view.querySelector('#progressSyncWatchHistory');
    const usersContainer = page.querySelector('#psHistoryUsers');
    const status = page.querySelector('#psHistoryStatus');
    const userCount = page.querySelector('#psHistoryUserCount');

    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 0, getTabs);
        loadUsers();
    });

    page.querySelector('#psRefreshHistory').addEventListener('click', loadUsers);

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
        const moviePanelId = 'ps-history-movies-' + userKey;
        const episodePanelId = 'ps-history-episodes-' + userKey;
        const tabs = document.createElement('div');
        tabs.className = 'ps-history-tabs';
        tabs.setAttribute('role', 'tablist');
        tabs.setAttribute('aria-label', user.Name + ' watch history');

        const movieTab = createHistoryTab('Movies', moviePanelId, true);
        const episodeTab = createHistoryTab('Episodes', episodePanelId, false);
        const movieGrid = createHistoryGrid(moviePanelId);
        const episodeGrid = createHistoryGrid(episodePanelId);
        movieGrid.hidden = true;
        episodeGrid.hidden = true;
        tabs.hidden = true;

        movieTab.addEventListener('click', function () {
            showHistoryTab(movieTab, movieGrid, episodeTab, episodeGrid);
        });
        episodeTab.addEventListener('click', function () {
            showHistoryTab(episodeTab, episodeGrid, movieTab, movieGrid);
        });
        tabs.addEventListener('keydown', function (event) {
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
                return;
            }

            event.preventDefault();
            const nextTab = event.key === 'ArrowRight' ? episodeTab : movieTab;
            const nextGrid = event.key === 'ArrowRight' ? episodeGrid : movieGrid;
            const previousTab = event.key === 'ArrowRight' ? movieTab : episodeTab;
            const previousGrid = event.key === 'ArrowRight' ? movieGrid : episodeGrid;
            showHistoryTab(nextTab, nextGrid, previousTab, previousGrid);
            nextTab.focus();
        });

        tabs.append(movieTab, episodeTab);
        content.append(message, tabs, movieGrid, episodeGrid);
        card.append(summary, content);
        card.addEventListener('toggle', function () {
            if (card.open && card.dataset.loaded !== 'true' && card.dataset.loading !== 'true') {
                loadUserHistory(user, card, hint, message, tabs, movieTab, movieGrid, episodeTab, episodeGrid);
            }
        });

        return card;
    }

    async function loadUserHistory(user, card, hint, message, tabs, movieTab, movieGrid, episodeTab, episodeGrid) {
        card.dataset.loading = 'true';
        message.textContent = 'Loading watch history…';
        message.hidden = false;
        hint.textContent = 'Loading…';
        try {
            const items = await ApiClient.getJSON(ApiClient.getUrl(
                'ProgressSync/RecentlyWatched/Users/' + encodeURIComponent(user.Id)));
            if (!card.isConnected) {
                return;
            }

            const movies = items.filter(item => item.ItemType === 'Movie');
            const episodes = items.filter(item => item.ItemType === 'Episode');
            populateHistoryGrid(movieGrid, movies, 'No recent movies.');
            populateHistoryGrid(episodeGrid, episodes, 'No recent episodes.');
            movieTab.textContent = 'Movies (' + movies.length + ')';
            episodeTab.textContent = 'Episodes (' + episodes.length + ')';
            message.hidden = true;
            tabs.hidden = false;
            showHistoryTab(movieTab, movieGrid, episodeTab, episodeGrid);
            hint.textContent = movies.length + ' movies · ' + episodes.length + ' episodes';
            card.dataset.loaded = 'true';
        } catch (error) {
            message.textContent = 'Could not load history (' + errorText(error) + ').';
            hint.textContent = 'Retry';
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

function showHistoryTab(selectedTab, selectedGrid, otherTab, otherGrid) {
    selectedTab.setAttribute('aria-selected', 'true');
    selectedTab.tabIndex = 0;
    otherTab.setAttribute('aria-selected', 'false');
    otherTab.tabIndex = -1;
    selectedGrid.hidden = false;
    otherGrid.hidden = true;
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
