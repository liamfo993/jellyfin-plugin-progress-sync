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

        const history = document.createElement('ul');
        history.className = 'ps-history';

        const message = document.createElement('p');
        message.className = 'ps-history-empty';
        message.textContent = 'Expand to load recent activity.';

        card.append(summary, history, message);
        card.addEventListener('toggle', function () {
            if (card.open && card.dataset.loaded !== 'true' && card.dataset.loading !== 'true') {
                loadUserHistory(user, card, hint, history, message);
            }
        });

        return card;
    }

    async function loadUserHistory(user, card, hint, history, message) {
        card.dataset.loading = 'true';
        message.textContent = 'Loading watch history…';
        hint.textContent = 'Loading…';
        try {
            const items = await ApiClient.getJSON(ApiClient.getUrl(
                'ProgressSync/RecentlyWatched/Users/' + encodeURIComponent(user.Id)));
            if (!card.isConnected) {
                return;
            }

            history.replaceChildren();
            if (items.length === 0) {
                message.textContent = 'No recent movies or episodes.';
            } else {
                const fragment = document.createDocumentFragment();
                for (const item of items) {
                    fragment.appendChild(createHistoryRow(item));
                }
                history.appendChild(fragment);
                message.textContent = '';
            }
            hint.textContent = items.length + (items.length === 1 ? ' item' : ' items');
            card.dataset.loaded = 'true';
        } catch (error) {
            message.textContent = 'Could not load history (' + errorText(error) + ').';
            hint.textContent = 'Retry';
            console.warn('[ProgressSync]', 'could not load a user watch history', error);
        } finally {
            delete card.dataset.loading;
        }
    }

    function createHistoryRow(item) {
        const row = document.createElement('li');
        row.className = 'ps-history-row';

        const type = document.createElement('span');
        type.className = 'ps-history-type';
        type.textContent = item.ItemType;

        const title = document.createElement('a');
        title.className = 'ps-history-title';
        title.href = '#/details?id=' + encodeURIComponent(item.ItemId);
        title.textContent = item.ItemName;

        const date = document.createElement('time');
        date.className = 'ps-history-date';
        date.dateTime = item.LastPlayedDate;
        date.textContent = formatDate(item.LastPlayedDate);

        row.append(type, title, date);
        return row;
    }
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
