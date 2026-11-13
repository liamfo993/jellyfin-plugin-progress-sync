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

function errorText(error) {
    return error && typeof error.status === 'number' ? 'HTTP ' + error.status : 'network error';
}

function dateLabel(month, day) {
    return new Date(2000, month - 1, day).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

const ALL_USERS_VALUE = 'all';

export default function (view) {
    const page = view.querySelector('#progressSyncMediaVisibility');
    const usersSelect = page.querySelector('#psVisibilityUser');
    const itemSelect = page.querySelector('#psVisibilityItem');
    const rulesContainer = page.querySelector('#psVisibilityRules');
    const rulesStatus = page.querySelector('#psVisibilityRulesStatus');
    const searchStatus = page.querySelector('#psVisibilitySearchStatus');
    const saveStatus = page.querySelector('#psVisibilitySaveStatus');
    const addButton = page.querySelector('#psAddVisibilityRule');
    const mediaTabs = page.querySelectorAll('.ps-type-tab');
    let activeMediaKind = 'all';

    LibraryMenu.setTabs('WatchHistory', 4, getTabs);

    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 4, getTabs);
        loadSettings();
    });

    mediaTabs.forEach(function (button) {
        button.addEventListener('click', function () {
            activeMediaKind = button.dataset.kind || 'all';
            mediaTabs.forEach(function (tab) {
                const isActive = tab === button;
                tab.classList.toggle('is-active', isActive);
                tab.setAttribute('aria-selected', String(isActive));
            });
            if (page.querySelector('#psVisibilitySearch').value.trim().length >= 2) {
                searchItems();
            }
        });
    });

    page.querySelector('#psVisibilitySearchButton').addEventListener('click', searchItems);
    page.querySelector('#psVisibilitySearch').addEventListener('keydown', function (event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            searchItems();
        }
    });
    page.querySelector('#psVisibilityScheduled').addEventListener('change', function (event) {
        page.querySelector('#psVisibilityDates').hidden = !event.target.checked;
    });
    page.querySelector('#psVisibilityForm').addEventListener('submit', addRule);
    rulesContainer.addEventListener('click', deleteRule);

    async function loadSettings() {
        rulesStatus.textContent = 'Loading users and rules…';
        try {
            const settings = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/Admin/Visibility'));
            renderUsers(settings.Users);
            renderRules(settings.Rules);
            rulesStatus.textContent = settings.Rules.length
                ? settings.Rules.length + ' hide rule' + (settings.Rules.length === 1 ? '' : 's') + ' configured.'
                : 'No media visibility rules configured.';
        } catch (error) {
            rulesStatus.textContent = 'Could not load media visibility rules (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not load media visibility rules', error);
        }
    }

    function renderUsers(users) {
        const fragment = document.createDocumentFragment();
        const allUsers = document.createElement('option');
        allUsers.value = ALL_USERS_VALUE;
        allUsers.textContent = 'Everyone (global rule)';
        fragment.appendChild(allUsers);
        users.forEach(function (user) {
            const option = document.createElement('option');
            option.value = user.Id;
            option.textContent = user.Name;
            fragment.appendChild(option);
        });
        usersSelect.replaceChildren(fragment);
        window.CustomElements.upgradeSubtree(usersSelect);
    }

    function renderRules(rules) {
        const fragment = document.createDocumentFragment();
        rules.forEach(function (rule) {
            const card = document.createElement('div');
            card.className = 'ps-rule';

            const details = document.createElement('div');
            details.className = 'ps-rule-details';
            const name = document.createElement('span');
            name.className = 'ps-rule-name';
            name.textContent = rule.ItemName;
            const target = document.createElement('span');
            target.className = 'fieldDescription';
            target.textContent = rule.UserId ? 'Hidden from ' + (rule.UserName || 'deleted user') : 'Hidden from everyone';
            const schedule = document.createElement('span');
            schedule.className = 'fieldDescription';
            schedule.textContent = rule.VisibleFromMonth
                ? 'Visible annually ' + dateLabel(rule.VisibleFromMonth, rule.VisibleFromDay)
                    + ' through ' + dateLabel(rule.VisibleThroughMonth, rule.VisibleThroughDay)
                    + '; hidden outside these dates.'
                : 'Always hidden.';
            details.append(name, target, schedule);

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'emby-button raised';
            remove.dataset.ruleId = rule.Id;
            const removeLabel = document.createElement('span');
            removeLabel.textContent = 'Remove';
            remove.appendChild(removeLabel);
            card.append(details, remove);
            fragment.appendChild(card);
        });

        if (rules.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'fieldDescription';
            empty.textContent = 'There are no media visibility rules yet.';
            fragment.appendChild(empty);
        }

        rulesContainer.replaceChildren(fragment);
    }

    async function searchItems() {
        const query = page.querySelector('#psVisibilitySearch').value.trim();
        if (query.length < 2) {
            searchStatus.textContent = 'Enter at least two characters to search.';
            return;
        }

        const kind = activeMediaKind === 'all' ? '' : activeMediaKind;
        searchStatus.textContent = 'Searching media…';
        itemSelect.replaceChildren();
        try {
            const url = new URL(ApiClient.getUrl('ProgressSync/Admin/Visibility/Items'));
            url.searchParams.set('search', query);
            if (kind) {
                url.searchParams.set('kind', kind);
            }

            const results = await ApiClient.getJSON(url.toString());
            const fragment = document.createDocumentFragment();
            const prompt = document.createElement('option');
            prompt.value = '';
            prompt.textContent = 'Choose a media item';
            fragment.appendChild(prompt);
            results.forEach(function (item) {
                const option = document.createElement('option');
                option.value = item.Id;
                option.textContent = item.Name + ' (' + item.Type + ')';
                fragment.appendChild(option);
            });
            itemSelect.replaceChildren(fragment);
            window.CustomElements.upgradeSubtree(itemSelect);
            searchStatus.textContent = results.length
                ? 'Found ' + results.length + ' item' + (results.length === 1 ? '' : 's') + '.'
                : 'No matching ' + (kind ? kind : 'media') + ' found.';
        } catch (error) {
            searchStatus.textContent = 'Could not search media (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not search media for visibility rules', error);
        }
    }

    async function addRule(event) {
        event.preventDefault();
        if (!itemSelect.value) {
            saveStatus.textContent = 'Search for and select a media item.';
            itemSelect.focus();
            return;
        }

        const scheduled = page.querySelector('#psVisibilityScheduled').checked;
        const from = page.querySelector('#psVisibilityFrom').value;
        const through = page.querySelector('#psVisibilityThrough').value;
        if (scheduled && (!from || !through)) {
            saveStatus.textContent = 'Choose both the first and final visible dates.';
            return;
        }

        const body = {
            ItemId: itemSelect.value,
            UserId: usersSelect.value === ALL_USERS_VALUE ? null : usersSelect.value,
            VisibleFromMonth: scheduled ? Number(from.slice(5, 7)) : null,
            VisibleFromDay: scheduled ? Number(from.slice(8, 10)) : null,
            VisibleThroughMonth: scheduled ? Number(through.slice(5, 7)) : null,
            VisibleThroughDay: scheduled ? Number(through.slice(8, 10)) : null
        };
        addButton.disabled = true;
        saveStatus.textContent = 'Applying visibility rule…';
        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/Visibility'),
                data: JSON.stringify(body),
                contentType: 'application/json'
            });
            saveStatus.textContent = 'Visibility rule added and applied.';
            page.querySelector('#psVisibilityScheduled').checked = false;
            page.querySelector('#psVisibilityDates').hidden = true;
            page.querySelector('#psVisibilityFrom').value = '';
            page.querySelector('#psVisibilityThrough').value = '';
            await loadSettings();
        } catch (error) {
            saveStatus.textContent = 'Could not apply visibility rule (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not add media visibility rule', error);
        } finally {
            addButton.disabled = false;
        }
    }

    async function deleteRule(event) {
        const button = event.target.closest('button[data-rule-id]');
        if (!button) {
            return;
        }

        button.disabled = true;
        rulesStatus.textContent = 'Removing visibility rule…';
        try {
            await ApiClient.ajax({
                type: 'DELETE',
                url: ApiClient.getUrl('ProgressSync/Admin/Visibility/' + encodeURIComponent(button.dataset.ruleId))
            });
            await loadSettings();
        } catch (error) {
            button.disabled = false;
            rulesStatus.textContent = 'Could not remove visibility rule (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not remove media visibility rule', error);
        }
    }
}
