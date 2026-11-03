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

export default function (view) {
    view.addEventListener('viewshow', function () {
        LibraryMenu.setTabs('WatchHistory', 2, getTabs);
    });
}
