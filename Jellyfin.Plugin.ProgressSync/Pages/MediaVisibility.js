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
    const itemsContainer = page.querySelector('#psVisibilityItems');
    const loadMoreButton = page.querySelector('#psVisibilityLoadMore');
    const rulesContainer = page.querySelector('#psVisibilityRules');
    const rulesStatus = page.querySelector('#psVisibilityRulesStatus');
    const searchStatus = page.querySelector('#psVisibilitySearchStatus');
    const selectionStatus = page.querySelector('#psVisibilitySelectionStatus');
    const saveStatus = page.querySelector('#psVisibilitySaveStatus');
    const addButton = page.querySelector('#psAddVisibilityRule');
    const mediaTabs = page.querySelectorAll('.ps-type-tab');
    const seriesItemsSelect = page.querySelector('#psVisibilitySeriesItems');
    const musicItemsSelect = page.querySelector('#psVisibilityMusicItems');
    const selectedItems = new Map();
    let activeMediaKind = 'all';
    let browseOffset = 0;
    let browseHasMore = false;
    let itemRequestId = 0;

    LibraryMenu.setTabs('WatchHistory', 4, getTabs);

    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 4, getTabs);
        loadSettings();
        loadBrowseItems(false);
    });

    mediaTabs.forEach(function (button) {
        button.addEventListener('click', function () {
            activeMediaKind = button.dataset.kind || 'all';
            mediaTabs.forEach(function (tab) {
                const isActive = tab === button;
                tab.classList.toggle('is-active', isActive);
                tab.setAttribute('aria-selected', String(isActive));
            });
            updateSeriesItemsControl();
            updateMusicItemsControl();
            refreshItems();
        });
    });

    page.querySelector('#psVisibilitySearchButton').addEventListener('click', searchItems);
    page.querySelector('#psVisibilityBrowseButton').addEventListener('click', function () {
        page.querySelector('#psVisibilitySearch').value = '';
        loadBrowseItems(false);
    });
    loadMoreButton.addEventListener('click', function () {
        loadBrowseItems(true);
    });
    page.querySelector('#psVisibilitySort').addEventListener('change', refreshItems);
    seriesItemsSelect.addEventListener('change', refreshItems);
    musicItemsSelect.addEventListener('change', refreshItems);
    itemsContainer.addEventListener('change', updateSelectedItem);
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
    updateSeriesItemsControl();
    updateMusicItemsControl();
    updateSelectionStatus();

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

            const poster = document.createElement('div');
            poster.className = 'ps-rule-poster';
            const image = document.createElement('img');
            image.alt = '';
            image.loading = 'lazy';
            image.src = ApiClient.getScaledImageUrl(rule.ItemId, { type: 'Primary', maxWidth: 120, quality: 85 });
            image.addEventListener('error', function () {
                const placeholder = document.createElement('span');
                placeholder.className = 'ps-item-placeholder';
                placeholder.textContent = rule.ItemType;
                poster.replaceChildren(placeholder);
            }, { once: true });
            poster.appendChild(image);

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
            card.append(poster, details, remove);
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
            searchStatus.textContent = 'Enter at least two characters to search, or choose Browse library.';
            return;
        }

        const requestId = ++itemRequestId;
        const kind = activeMediaKind === 'all' ? '' : activeMediaKind;
        searchStatus.textContent = 'Searching media…';
        itemsContainer.replaceChildren();
        loadMoreButton.hidden = true;
        try {
            const url = new URL(ApiClient.getUrl('ProgressSync/Admin/Visibility/Items'));
            url.searchParams.set('search', query);
            setItemFilters(url);

            const results = await ApiClient.getJSON(url.toString());
            if (requestId !== itemRequestId) {
                return;
            }

            renderItems(results);
            searchStatus.textContent = results.length
                ? 'Found ' + results.length + ' item' + (results.length === 1 ? '' : 's') + '. Check the posters you want.'
                : 'No matching ' + (kind ? kind : 'media') + ' found.';
        } catch (error) {
            if (requestId !== itemRequestId) {
                return;
            }

            searchStatus.textContent = 'Could not search media (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not search media for visibility rules', error);
        }
    }

    function setItemFilters(url) {
        if (activeMediaKind !== 'all') {
            url.searchParams.set('kind', activeMediaKind);
        }

        if (activeMediaKind === 'music') {
            url.searchParams.set('musicItems', musicItemsSelect.value);
        }

        if (activeMediaKind === 'series') {
            url.searchParams.set('seriesItems', seriesItemsSelect.value);
        }

        url.searchParams.set('sort', page.querySelector('#psVisibilitySort').value);
    }

    function updateSeriesItemsControl() {
        page.querySelector('#psVisibilitySeriesItemsField').hidden = activeMediaKind !== 'series';
    }

    function updateMusicItemsControl() {
        page.querySelector('#psVisibilityMusicItemsField').hidden = activeMediaKind !== 'music';
    }

    function refreshItems() {
        if (page.querySelector('#psVisibilitySearch').value.trim().length >= 2) {
            searchItems();
        } else {
            loadBrowseItems(false);
        }
    }

    async function loadBrowseItems(append) {
        if (append && loadMoreButton.disabled) {
            return;
        }

        const requestId = ++itemRequestId;
        const startIndex = append ? browseOffset : 0;
        if (!append) {
            browseOffset = 0;
            browseHasMore = false;
            itemsContainer.replaceChildren();
        }

        loadMoreButton.disabled = true;
        loadMoreButton.hidden = true;
        searchStatus.textContent = append ? 'Loading more media…' : 'Loading library posters…';
        try {
            const url = new URL(ApiClient.getUrl('ProgressSync/Admin/Visibility/Browse'));
            url.searchParams.set('startIndex', String(startIndex));
            setItemFilters(url);

            const result = await ApiClient.getJSON(url.toString());
            if (requestId !== itemRequestId) {
                return;
            }

            browseOffset = startIndex + result.Items.length;
            browseHasMore = result.HasMore;
            renderItems(result.Items, append);
            searchStatus.textContent = browseOffset
                ? 'Showing ' + browseOffset + ' items' + (browseHasMore ? ' so far. Load more to continue.' : '. Check posters to select them.')
                : 'No media found in this category.';
            loadMoreButton.hidden = !browseHasMore;
        } catch (error) {
            if (requestId !== itemRequestId) {
                return;
            }

            searchStatus.textContent = 'Could not browse media (' + errorText(error) + ').';
            loadMoreButton.hidden = !append || !browseHasMore;
            console.warn('[ProgressSync]', 'could not browse media for visibility rules', error);
        } finally {
            if (requestId === itemRequestId) {
                loadMoreButton.disabled = false;
            }
        }
    }

    function renderItems(items, append) {
        if (!append) {
            itemsContainer.replaceChildren();
        }

        const fragment = document.createDocumentFragment();
        items.forEach(function (item) {
            fragment.appendChild(createItemCard(item));
        });
        itemsContainer.appendChild(fragment);
    }

    function createItemCard(item) {
        const card = document.createElement('div');
        card.className = 'ps-item-card';
        card.dataset.itemType = item.Type;

        const poster = document.createElement('div');
        poster.className = 'ps-item-poster';
        const image = document.createElement('img');
        image.alt = '';
        image.loading = 'lazy';
        image.src = ApiClient.getScaledImageUrl(item.Id, { type: 'Primary', maxWidth: 300, quality: 85 });
        image.addEventListener('error', function () {
            const placeholder = document.createElement('span');
            placeholder.className = 'ps-item-placeholder';
            placeholder.textContent = item.Type;
            poster.replaceChildren(placeholder);
        }, { once: true });
        poster.appendChild(image);

        const choice = document.createElement('label');
        choice.className = 'ps-item-choice';
        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.dataset.itemId = item.Id;
        checkbox.checked = selectedItems.has(item.Id);
        checkbox.setAttribute('aria-label', 'Select ' + item.Name);
        const name = document.createElement('span');
        name.className = 'ps-item-name';
        name.textContent = item.Name;
        choice.append(checkbox, name);
        card.append(poster, choice);
        return card;
    }

    function updateSelectedItem(event) {
        const checkbox = event.target.closest('input[type="checkbox"][data-item-id]');
        if (!checkbox) {
            return;
        }

        const itemId = checkbox.dataset.itemId;
        if (checkbox.checked) {
            if (selectedItems.size >= 500) {
                checkbox.checked = false;
                saveStatus.textContent = 'Select no more than 500 items per batch.';
                return;
            }

            const itemName = checkbox.closest('.ps-item-choice').querySelector('.ps-item-name').textContent;
            const card = checkbox.closest('.ps-item-card');
            selectedItems.set(itemId, {
                Id: itemId,
                Name: itemName,
                Type: card.dataset.itemType
            });
        } else {
            selectedItems.delete(itemId);
        }

        updateSelectionStatus();
    }

    function updateSelectionStatus() {
        const count = selectedItems.size;
        selectionStatus.textContent = count
            ? count + ' item' + (count === 1 ? '' : 's') + ' selected.'
            : 'No items selected.';
    }

    async function addRule(event) {
        event.preventDefault();
        if (!selectedItems.size) {
            saveStatus.textContent = 'Browse or search, then check at least one media item.';
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
            ItemIds: Array.from(selectedItems.keys()),
            UserId: usersSelect.value === ALL_USERS_VALUE ? null : usersSelect.value,
            VisibleFromMonth: scheduled ? Number(from.slice(5, 7)) : null,
            VisibleFromDay: scheduled ? Number(from.slice(8, 10)) : null,
            VisibleThroughMonth: scheduled ? Number(through.slice(5, 7)) : null,
            VisibleThroughDay: scheduled ? Number(through.slice(8, 10)) : null
        };
        addButton.disabled = true;
        saveStatus.textContent = 'Applying visibility rules…';
        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/Visibility/Bulk'),
                data: JSON.stringify(body),
                contentType: 'application/json'
            });
            const addedCount = selectedItems.size;
            selectedItems.clear();
            itemsContainer.querySelectorAll('input[data-item-id]').forEach(function (checkbox) {
                checkbox.checked = false;
            });
            updateSelectionStatus();
            saveStatus.textContent = 'Added visibility rules for ' + addedCount + ' item' + (addedCount === 1 ? '' : 's') + '.';
            page.querySelector('#psVisibilityScheduled').checked = false;
            page.querySelector('#psVisibilityDates').hidden = true;
            page.querySelector('#psVisibilityFrom').value = '';
            page.querySelector('#psVisibilityThrough').value = '';
            await loadSettings();
        } catch (error) {
            saveStatus.textContent = 'Could not apply visibility rules (' + errorText(error) + ').';
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
