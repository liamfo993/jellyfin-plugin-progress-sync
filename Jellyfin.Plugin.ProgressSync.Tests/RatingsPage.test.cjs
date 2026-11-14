const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../Jellyfin.Plugin.ProgressSync/Pages/Ratings.js'), 'utf8');

test('settings report the dependency and save the selected option', async () => {
    const elements = new Map();
    for (const id of ['#psRatingsEnabled', '#psSaveRatings', '#psRatingsSaveStatus', '#psRatingsDependencyStatus', '#psRatingsForm']) {
        elements.set(id, {
            textContent: '',
            checked: false,
            disabled: false,
            addEventListener(name, handler) { this[name] = handler; }
        });
    }
    const page = { querySelector: id => elements.get(id) };
    const view = {
        querySelector: () => page,
        addEventListener(name, handler) { this[name] = handler; }
    };
    let update;
    const context = {
        LibraryMenu: { setTabs() {} },
        ApiClient: {
            getUrl: route => route,
            async getJSON(route) {
                if (route === 'Social/Capabilities') {
                    throw { status: 404 };
                }
                return { Enabled: false };
            },
            async ajax(request) { update = request; }
        },
        console: { warn() {} }
    };
    vm.runInNewContext(source.replace('export default function (view)', 'globalThis.init = function (view)'), context);
    context.init(view);
    view.viewshow();
    await new Promise(resolve => setImmediate(resolve));
    assert.match(elements.get('#psRatingsDependencyStatus').textContent, /could not be detected \(HTTP 404\)/);
    assert.equal(elements.get('#psRatingsEnabled').disabled, false);
    elements.get('#psRatingsEnabled').checked = true;
    await elements.get('#psRatingsForm').submit({ preventDefault() {} });
    assert.equal(update.url, 'ProgressSync/Admin/WatchHistoryRatings');
    assert.equal(JSON.parse(update.data).Enabled, true);
    assert.match(elements.get('#psRatingsSaveStatus').textContent, /saved/i);
});
