const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(
    path.join(__dirname, '../Jellyfin.Plugin.ProgressSync/Web/watchHistoryMenu.js'),
    'utf8'
);
const dashboardSource = fs.readFileSync(
    path.join(__dirname, '../Jellyfin.Plugin.ProgressSync/Pages/WatchHistory.js'),
    'utf8'
);

class Element {
    constructor(tagName = 'div') {
        this.tagName = tagName;
        this.children = [];
        this.dataset = {};
        this.style = {};
        this.attributes = {};
        this.listeners = {};
        this.classList = { add: (...names) => { this.className = [this.className, ...names].filter(Boolean).join(' '); } };
        this.isConnected = true;
        this.hidden = false;
        this.textContent = '';
        this.id = '';
    }

    addEventListener(name, callback) {
        this.listeners[name] = callback;
    }

    setAttribute(name, value) {
        this.attributes[name] = value;
    }

    append(...elements) {
        for (const element of elements) {
            this.appendChild(element);
        }
    }

    appendChild(element) {
        if (element.isFragment) {
            this.children.push(...element.children);
        } else {
            this.children.push(element);
        }
        return element;
    }

    replaceChildren(...elements) {
        this.children = [];
        this.append(...elements);
    }

    querySelector(selector) {
        return this.querySelectorAll(selector)[0] || null;
    }

    querySelectorAll(selector) {
        const matches = [];
        const className = selector.startsWith('.') ? selector.slice(1) : null;
        const id = selector.startsWith('#') ? selector.slice(1) : null;
        const visit = element => {
            if ((className && element.className?.split(' ').includes(className))
                || (id && element.id === id)) {
                matches.push(element);
            }
            element.children.forEach(visit);
        };
        this.children.forEach(visit);
        return matches;
    }
}

function setup(compactLayout, getJSON) {
    const dialog = new Element();
    const dialogElements = new Map([
        ['.btnCancel', new Element('button')],
        ['.ps-wh-status', new Element('p')],
        ['.ps-wh-users', new Element('div')]
    ]);
    dialog.querySelector = selector => dialogElements.get(selector) || null;
    const options = [];
    const context = {
        document: {
            createElement: tagName => new Element(tagName),
            createDocumentFragment() {
                const fragment = new Element();
                fragment.isFragment = true;
                return fragment;
            }
        },
        window: { matchMedia: () => ({ matches: compactLayout }) },
        console,
        Date,
        Intl,
        Map,
        Set,
        Promise
    };
    vm.runInNewContext(
        source.replace('export default function openWatchHistory', 'globalThis.openWatchHistory = function openWatchHistory'),
        context
    );

    const apiClient = {
        getUrl: route => route,
        async getJSON(route) {
            if (getJSON) {
                return getJSON(route);
            }
            if (route.endsWith('/RecentlyWatched/Users')) {
                return [{ Id: 'alice', Name: 'Alice' }];
            }
            return [];
        },
        getScaledImageUrl: () => ''
    };
    const dialogHelper = {
        createDialog(option) {
            options.push(option);
            dialog.innerHTML = '';
            return dialog;
        },
        open() {},
        close() {}
    };
    context.openWatchHistory(apiClient, dialogHelper);
    return { dialog, dialogElements, options };
}

test('uses a fullscreen dialog on compact layouts', () => {
    const { options } = setup(true);
    assert.equal(options[0].size, 'fullscreen');
    assert.equal(setup(false).options[0].size, 'small');
});

test('compact dialog styles remove desktop size limits', () => {
    const { dialog } = setup(true);
    const compactRule = dialog.innerHTML.match(/@media\(max-width:80em\),\(max-height:45em\)\{\.ps-watch-history-dialog\{([^}]*)\}\}/);
    assert.ok(compactRule);
    for (const declaration of [
        'top:0!important', 'right:0!important', 'bottom:0!important', 'left:0!important',
        'width:auto!important', 'height:auto!important', 'max-width:none!important', 'max-height:none!important'
    ]) {
        assert.ok(compactRule[1].includes(declaration), `Missing ${declaration}`);
    }
});

test('history tabs switch panels and hidden panels have an explicit display rule', async () => {
    const { dialog, dialogElements } = setup(false);
    await new Promise(resolve => setImmediate(resolve));
    const card = dialogElements.get('.ps-wh-users').children[0];
    card.open = true;
    card.listeners.toggle();
    await new Promise(resolve => setImmediate(resolve));

    const content = card.children[1];
    const tabs = content.children[1].children;
    const grids = content.children.slice(2);
    assert.equal(grids[0].hidden, false);
    assert.equal(grids[1].hidden, true);

    tabs[1].listeners.click();
    assert.equal(grids[0].hidden, true);
    assert.equal(grids[1].hidden, false);

    assert.match(source, /\.ps-watch-history-dialog \[hidden\]\{display:none!important\}/);
    assert.match(dashboardSource, /viewConfig\.key === selectedView\.key/);
});

test('watched tabs render without waiting for in-progress history', async () => {
    let finishInProgress;
    const pending = new Promise(resolve => { finishInProgress = resolve; });
    const { dialogElements } = setup(false, route => {
        if (route.endsWith('/RecentlyWatched/Users')) {
            return [{ Id: 'alice', Name: 'Alice' }];
        }
        return route.includes('/InProgress/') ? pending : [];
    });
    await new Promise(resolve => setImmediate(resolve));
    const card = dialogElements.get('.ps-wh-users').children[0];
    card.open = true;
    card.listeners.toggle();
    await new Promise(resolve => setImmediate(resolve));
    const content = card.children[1];
    assert.equal(content.children[2].children[0].textContent, 'No recent movies.');
    assert.equal(content.children[4].children[0].textContent, 'Loading…');
    finishInProgress([]);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(content.children[4].children[0].textContent, 'No in-progress movies.');
});

test('dashboard renders watched history before in-progress completes', async () => {
    let finishInProgress;
    const pending = new Promise(resolve => { finishInProgress = resolve; });
    const elements = new Map([
        '#psHistoryUsers', '#psHistoryStatus', '#psHistoryUserCount', '#psShowWatchHistoryMenu',
        '#psWatchHistoryMenuStatus', '#psSaveWatchHistoryMenu', '#psVisibilityUsers',
        '#psVisibilityStatus', '#psSaveVisibility', '#psRefreshHistory'
    ].map(id => [id, new Element()]));
    const page = { querySelector: id => elements.get(id) };
    const view = new Element();
    view.querySelector = () => page;
    const context = {
        document: {
            createElement: tagName => new Element(tagName),
            createDocumentFragment() {
                const fragment = new Element();
                fragment.isFragment = true;
                return fragment;
            }
        },
        LibraryMenu: { setTabs() {} },
        ApiClient: {
            getUrl: route => route,
            async getJSON(route) {
                if (route.endsWith('/RecentlyWatched/Users')) {
                    return [{ Id: 'alice', Name: 'Alice' }];
                }
                if (route.includes('/InProgress/')) {
                    return pending;
                }
                if (route.endsWith('/WatchHistoryVisibility')) {
                    return { Users: [], Rules: [] };
                }
                if (route.endsWith('/WatchHistoryMenu')) {
                    return { Enabled: false };
                }
                return [];
            }
        },
        console,
        Date,
        Intl,
        Map,
        Set,
        Promise
    };
    vm.runInNewContext(dashboardSource.replace('export default function (view)', 'globalThis.init = function (view)'), context);
    context.init(view);
    view.listeners.viewshow();
    await new Promise(resolve => setImmediate(resolve));
    const card = elements.get('#psHistoryUsers').children[0];
    card.open = true;
    card.listeners.toggle();
    await new Promise(resolve => setImmediate(resolve));
    const content = card.children[1];
    assert.equal(content.children[2].children[0].textContent, 'No recent movies.');
    assert.equal(content.children[4].children[0].textContent, 'Loading…');
    finishInProgress([]);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(content.children[4].children[0].textContent, 'No in-progress movies.');
});
