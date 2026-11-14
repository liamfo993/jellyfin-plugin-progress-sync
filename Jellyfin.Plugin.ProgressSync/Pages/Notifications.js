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
    const page = view.querySelector('#progressSyncNotifications');
    const form = page.querySelector('#psPushoverForm');
    const status = page.querySelector('#psPushoverCredentialStatus');
    const saveStatus = page.querySelector('#psPushoverSaveStatus');
    const saveButton = page.querySelector('#psSavePushover');
    const testButton = page.querySelector('#psTestPushover');
    let isConfigured = false;
    const eventFields = {
        NotifyAdded: '#psNotifyAdded',
        NotifyWatched: '#psNotifyWatched',
        NotifyDeleted: '#psNotifyDeleted',
        NotifyPlaybackStarted: '#psNotifyPlaybackStarted',
        NotifyPlaybackStopped: '#psNotifyPlaybackStopped'
    };

    LibraryMenu.setTabs('WatchHistory', 2, getTabs);

    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 2, getTabs);
        loadSettings();
    });

    form.addEventListener('submit', saveSettings);
    testButton.addEventListener('click', sendTest);

    async function loadSettings() {
        status.textContent = 'Loading Pushover settings…';
        try {
            const settings = await ApiClient.getJSON(ApiClient.getUrl('ProgressSync/Admin/Pushover'));
            isConfigured = settings.IsConfigured;
            Object.keys(eventFields).forEach(function (name) {
                page.querySelector(eventFields[name]).checked = settings[name];
            });
            testButton.disabled = !settings.IsConfigured;
            status.textContent = settings.IsConfigured
                ? 'Pushover credentials are configured. Saved credentials are hidden.'
                : 'Pushover credentials are not configured.';
        } catch (error) {
            status.textContent = 'Could not load Pushover settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not load Pushover settings', error);
        }
    }

    async function saveSettings(event) {
        event.preventDefault();
        const update = {
            AppToken: page.querySelector('#psPushoverAppToken').value,
            UserKey: page.querySelector('#psPushoverUserKey').value,
            ClearCredentials: page.querySelector('#psPushoverClearCredentials').checked
        };
        Object.keys(eventFields).forEach(function (name) {
            update[name] = page.querySelector(eventFields[name]).checked;
        });
        const hasEvents = Object.keys(eventFields).some(function (name) {
            return update[name];
        });
        const willBeConfigured = !update.ClearCredentials
            && (isConfigured || (update.AppToken.trim() && update.UserKey.trim()));
        if (hasEvents && !willBeConfigured) {
            saveStatus.textContent = update.ClearCredentials
                ? 'Turn off all event notifications before clearing credentials.'
                : 'Enter both Pushover credentials before enabling notifications.';
            return;
        }

        saveButton.disabled = true;
        testButton.disabled = true;
        saveStatus.textContent = 'Saving Pushover settings…';

        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/Pushover'),
                data: JSON.stringify(update),
                contentType: 'application/json'
            });
            page.querySelector('#psPushoverAppToken').value = '';
            page.querySelector('#psPushoverUserKey').value = '';
            page.querySelector('#psPushoverClearCredentials').checked = false;
            saveStatus.textContent = 'Pushover settings saved.';
            await loadSettings();
        } catch (error) {
            saveStatus.textContent = 'Could not save Pushover settings (' + errorText(error) + ').';
            console.warn('[ProgressSync]', 'could not save Pushover settings', error);
        } finally {
            saveButton.disabled = false;
            testButton.disabled = !isConfigured;
        }
    }

    async function sendTest() {
        testButton.disabled = true;
        saveStatus.textContent = 'Sending test notification…';
        try {
            await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ProgressSync/Admin/Pushover/Test')
            });
            saveStatus.textContent = 'Test notification sent.';
        } catch (error) {
            saveStatus.textContent = 'Could not send test notification (' + errorText(error) + '). Verify credentials and server connectivity.';
            console.warn('[ProgressSync]', 'Pushover test notification failed', error);
        } finally {
            testButton.disabled = false;
        }
    }
}
