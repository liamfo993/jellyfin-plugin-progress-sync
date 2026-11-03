function configurationPageUrl(name) {
    return 'configurationpage?name=' + encodeURIComponent(name);
}

function getTabs() {
    return [
        { href: configurationPageUrl('WatchHistory'), name: 'Watch history' },
        { href: configurationPageUrl('PlaybackLimits'), name: 'Playback limits' },
        { href: configurationPageUrl('ProgressSync'), name: 'Progress sync' }
    ];
}

function errorText(error) {
    return error && typeof error.status === 'number' ? 'HTTP ' + error.status : 'network error';
}

export default function (view) {
    const page = view.querySelector('#progressSyncPlaybackLimits');
    const usersContainer = page.querySelector('#psStreamLimits');
    const loadStatus = page.querySelector('#psLimitsStatus');
    const saveStatus = page.querySelector('#psStreamSaveStatus');
    const saveButton = page.querySelector('#psSaveStreamLimits');

    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 1, getTabs);
        loadLimits();
    });

    page.querySelector('#psRefreshLimits').addEventListener('click', loadLimits);
    page.querySelector('#psStreamLimitsForm').addEventListener('submit', saveLimits);

    async function loadLimits() {
        loadStatus.textContent = 'Loading users and saved limits…';
        usersContainer.replaceChildren();
        saveStatus.textContent = '';
        try {
            const data = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/Admin/StreamLimits'));
            renderLimits(data);
            loadStatus.textContent = data.Users.length
                ? 'Limits loaded for ' + data.Users.length + ' enabled users.'
                : 'No enabled users found.';
        } catch (error) {
            loadStatus.textContent = 'Could not load playback limits (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not load playback limits', error);
        }
    }

    function renderLimits(data) {
        const limitsByUser = new Map(data.Limits.map(function (limit) {
            return [limit.UserId, limit.MaxConcurrentStreams];
        }));
        const fragment = document.createDocumentFragment();
        data.Users.forEach(function (user) {
            const card = document.createElement('div');
            card.className = 'ps-limit-card';

            const userDetails = document.createElement('div');
            userDetails.className = 'ps-limit-user';
            const avatar = document.createElement('span');
            avatar.className = 'ps-user-avatar';
            avatar.textContent = user.Name.trim().charAt(0).toLocaleUpperCase() || '?';
            const name = document.createElement('span');
            name.className = 'ps-limit-name';
            name.textContent = user.Name;
            userDetails.append(avatar, name);

            const control = document.createElement('label');
            control.className = 'ps-limit-control';
            const input = document.createElement('input');
            input.type = 'number';
            input.min = '0';
            input.max = '100';
            input.step = '1';
            input.value = String(limitsByUser.get(user.Id) || 0);
            input.dataset.userId = user.Id;
            input.className = 'emby-input ps-limit-input';
            input.setAttribute('aria-label', 'Maximum concurrent streams for ' + user.Name);
            const description = document.createElement('span');
            description.className = 'ps-limit-description';
            setLimitDescription(description, input.value);
            input.addEventListener('input', function () {
                setLimitDescription(description, input.value);
            });
            control.append(input, description);
            card.append(userDetails, control);
            fragment.appendChild(card);
        });

        if (data.Users.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'ps-empty';
            empty.textContent = 'There are no enabled users to configure.';
            fragment.appendChild(empty);
        }

        usersContainer.replaceChildren(fragment);
    }

    function setLimitDescription(element, value) {
        const limit = Number(value);
        if (value.trim() === '' || !Number.isInteger(limit) || limit < 0 || limit > 100) {
            element.textContent = 'Enter a whole number from 0 to 100.';
            return;
        }

        element.textContent = limit > 0
            ? 'Maximum ' + limit + (limit === 1 ? ' active stream' : ' active streams')
            : 'No limit';
    }

    async function saveLimits(event) {
        event.preventDefault();
        const limits = [];
        for (const input of page.querySelectorAll('.ps-limit-input')) {
            const value = Number(input.value);
            if (input.value.trim() === '' || !Number.isInteger(value) || value < 0 || value > 100) {
                saveStatus.textContent = 'Enter a whole number from 0 to 100 for every user.';
                input.focus();
                return;
            }

            if (value > 0) {
                limits.push({ UserId: input.dataset.userId, MaxConcurrentStreams: value });
            }
        }

        saveButton.disabled = true;
        saveStatus.textContent = 'Saving playback limits…';
        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/StreamLimits'),
                data: JSON.stringify(limits),
                contentType: 'application/json'
            });
            saveStatus.textContent = 'Playback limits saved.';
        } catch (error) {
            saveStatus.textContent = 'Could not save playback limits (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not save playback limits', error);
        } finally {
            saveButton.disabled = false;
        }
    }
}
