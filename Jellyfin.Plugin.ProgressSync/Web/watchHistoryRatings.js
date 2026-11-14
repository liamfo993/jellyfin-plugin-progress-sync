export default async function addWatchHistoryRatings(apiClient, grids) {
    const settings = await apiClient.getJSON(apiClient.getUrl('ProgressSync/WatchHistoryRatings'));
    if (!settings.Enabled) {
        return;
    }

    const cards = grids.flatMap(grid => Array.from(grid.querySelectorAll('[data-rating-item-id]')));
    const ids = [...new Set(cards.map(card => card.dataset.ratingItemId))];
    if (ids.length === 0) {
        return;
    }

    const capabilities = await apiClient.getJSON(apiClient.getUrl('Social/Capabilities'));
    if (!capabilities || capabilities.plugin !== 'Jellyfin.Plugin.Ratings'
        || !capabilities.enabled || !capabilities.enabled.ratings) {
        throw new Error('Ratings plugin is unavailable or its ratings feature is disabled');
    }

    const stats = new Map();
    for (let offset = 0; offset < ids.length; offset += 100) {
        const batch = ids.slice(offset, offset + 100);
        const url = new URL(apiClient.getUrl('Ratings/Items/BatchStats'));
        url.searchParams.set('itemIds', batch.join(','));
        const response = await apiClient.getJSON(url.toString());
        if (!response || typeof response !== 'object' || Array.isArray(response)) {
            throw new Error('Invalid Ratings plugin response');
        }
        for (const [id, value] of Object.entries(response)) {
            stats.set(id.replace(/-/g, '').toLowerCase(), value);
        }
    }

    for (const card of cards) {
        const rating = stats.get(card.dataset.ratingItemId.replace(/-/g, '').toLowerCase());
        if (!rating || !(rating.totalRatings > 0) || !Number.isFinite(rating.averageRating)
            || rating.averageRating < 0 || rating.averageRating > 10) {
            continue;
        }
        const poster = card.querySelector('.ps-wh-poster, .ps-history-poster');
        if (!poster || poster.querySelector('.ps-history-rating')) {
            continue;
        }
        const badge = document.createElement('span');
        badge.className = 'ps-history-rating';
        badge.textContent = '★ ' + rating.averageRating.toFixed(1);
        badge.setAttribute('aria-label', 'Community rating ' + rating.averageRating.toFixed(1) + ' out of 10');
        badge.style.cssText = 'position:absolute;top:.4em;left:.4em;z-index:1;padding:.25em .45em;border-radius:.35em;background:rgba(0,0,0,.8);color:#ffd75e;font-size:.85em;font-weight:700;line-height:1.2';
        poster.appendChild(badge);
    }
}
