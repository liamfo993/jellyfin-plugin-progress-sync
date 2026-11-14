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

function errorText(error) {
    return error && typeof error.status === 'number' ? 'HTTP ' + error.status : 'network error';
}

export default function (view) {
    const page = view.querySelector('#progressSyncRatings');
    const checkbox = page.querySelector('#psRatingsEnabled');
    const save = page.querySelector('#psSaveRatings');
    const status = page.querySelector('#psRatingsSaveStatus');
    const dependency = page.querySelector('#psRatingsDependencyStatus');

    LibraryMenu.setTabs('WatchHistory', 5, getTabs);
    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 5, getTabs);
        loadSettings();
        checkDependency();
    });
    page.querySelector('#psRatingsForm').addEventListener('submit', async function (event) {
        event.preventDefault();
        save.disabled = true;
        status.textContent = 'Saving ratings settings…';
        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/WatchHistoryRatings'),
                data: JSON.stringify({ Enabled: checkbox.checked }),
                contentType: 'application/json'
            });
            status.textContent = 'Ratings settings saved. Reopen Watch History to apply changes.';
        } catch (error) {
            status.textContent = 'Could not save ratings settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not save ratings settings', error);
        } finally {
            save.disabled = false;
        }
    });

    async function loadSettings() {
        checkbox.disabled = true;
        save.disabled = true;
        status.textContent = 'Loading ratings settings…';
        try {
            const settings = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/WatchHistoryRatings'));
            checkbox.checked = settings.Enabled;
            checkbox.disabled = false;
            save.disabled = false;
            status.textContent = '';
        } catch (error) {
            status.textContent = 'Could not load ratings settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not load ratings settings', error);
        }
    }

    async function checkDependency() {
        dependency.textContent = 'Checking Ratings plugin…';
        try {
            const capabilities = await ApiClient.getJSON(ApiClient.getUrl('Social/Capabilities'));
            if (capabilities.plugin !== 'Jellyfin.Plugin.Ratings') {
                throw new Error('Unexpected capabilities response');
            }
            dependency.textContent = capabilities.enabled && capabilities.enabled.ratings
                ? 'Ratings plugin detected (version ' + capabilities.version + ').'
                : 'Ratings plugin detected, but its ratings feature is disabled.';
        } catch (error) {
            dependency.textContent = 'Ratings plugin could not be detected (' + errorText(error) + '). Install or update it and enable its ratings feature.';
            console.warn('[ProgressSync]', 'could not detect Ratings plugin', error);
        }
    }
}
