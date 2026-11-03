function configurationPageUrl(name) {
    return 'configurationpage?name=' + encodeURIComponent(name);
}

function getTabs() {
    return [
        { href: configurationPageUrl('WatchHistory'), name: 'Watch history' },
        { href: configurationPageUrl('PlaybackLimits'), name: 'Playback limits' },
        { href: configurationPageUrl('Notifications'), name: 'Notifications' },
        { href: configurationPageUrl('ProgressSync'), name: 'Progress sync' }
    ];
}

export default function (view) {
    LibraryMenu.setTabs('WatchHistory', 3, getTabs);

    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 3, getTabs);
    });
}
