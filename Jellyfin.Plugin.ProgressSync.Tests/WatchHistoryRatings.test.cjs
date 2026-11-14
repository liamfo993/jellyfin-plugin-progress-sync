const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(
    path.join(__dirname, '../Jellyfin.Plugin.ProgressSync/Web/watchHistoryRatings.js'),
    'utf8'
);

function setup(enabled, items, batchResponse, capabilities = { plugin: 'Jellyfin.Plugin.Ratings', enabled: { ratings: true } }) {
    const calls = [];
    const cards = items.map(id => {
        const poster = {
            children: [],
            querySelector: () => poster.children[0] || null,
            appendChild: child => poster.children.push(child)
        };
        return {
            dataset: { ratingItemId: id },
            poster,
            querySelector: () => poster
        };
    });
    const context = {
        URL,
        document: {
            createElement: () => ({
                style: {},
                setAttribute(name, value) { this[name] = value; }
            })
        }
    };
    vm.runInNewContext(source.replace('export default async function addWatchHistoryRatings', 'globalThis.addWatchHistoryRatings = async function addWatchHistoryRatings'), context);
    const client = {
        getUrl: route => 'http://localhost/' + route,
        async getJSON(request) {
            const url = new URL(request);
            calls.push(url);
            if (url.pathname === '/ProgressSync/WatchHistoryRatings') {
                return { Enabled: enabled };
            }
            if (url.pathname === '/Social/Capabilities') {
                return capabilities;
            }
            return batchResponse(url.searchParams.get('itemIds').split(','));
        }
    };
    return { run: () => context.addWatchHistoryRatings(client, [{ querySelectorAll: () => cards }]), calls, cards };
}

test('does not request Ratings plugin data when disabled', async () => {
    const fixture = setup(false, ['abc'], () => { throw new Error('unexpected request'); });
    await fixture.run();
    assert.equal(fixture.calls.length, 1);
    assert.equal(fixture.cards[0].poster.children.length, 0);
});

test('shows community averages on rated covers, batches requests and normalizes IDs', async () => {
    const ids = Array.from({ length: 101 }, (_, index) => index.toString(16).padStart(32, '0'));
    const dashed = ids[0].slice(0, 8) + '-' + ids[0].slice(8);
    const fixture = setup(true, [dashed, ids[1], ...ids.slice(2), dashed], batch => Object.fromEntries(
        batch.map(id => [id.replaceAll('-', ''), { averageRating: 7.25, totalRatings: id === ids[1] ? 0 : 2 }])
    ));
    await fixture.run();
    assert.equal(fixture.calls.length, 4);
    assert.equal(fixture.calls[2].searchParams.get('itemIds').split(',').length, 100);
    assert.equal(fixture.calls[3].searchParams.get('itemIds').split(',').length, 1);
    assert.equal(fixture.cards[0].poster.children[0].textContent, '★ 7.3');
    assert.equal(fixture.cards[0].poster.children[0]['aria-label'], 'Community rating 7.3 out of 10');
    assert.equal(fixture.cards[1].poster.children.length, 0);
    assert.equal(fixture.cards.at(-1).poster.children.length, 1);
});

test('surfaces Ratings API failures', async () => {
    const fixture = setup(true, ['abc'], () => { throw new Error('Ratings unavailable'); });
    await assert.rejects(fixture.run(), /Ratings unavailable/);
});

test('does not request ratings when the Ratings feature is disabled', async () => {
    const fixture = setup(true, ['abc'], () => { throw new Error('unexpected request'); },
        { plugin: 'Jellyfin.Plugin.Ratings', enabled: { ratings: false } });
    await assert.rejects(fixture.run(), /ratings feature is disabled/);
    assert.equal(fixture.calls.length, 2);
});
