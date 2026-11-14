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

    getAttribute(name) {
        return this.attributes[name] ?? null;
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

function fakeTimers() {
    let nextId = 0;
    const callbacks = new Map();
    return {
        setTimeout(callback) { const id = ++nextId; callbacks.set(id, callback); return id; },
        clearTimeout(id) { callbacks.delete(id); },
        run() { const pending = [...callbacks.values()]; callbacks.clear(); pending.forEach(callback => callback()); },
        count() { return callbacks.size; }
    };
}

function setup(compactLayout, getJSON, timers = fakeTimers()) {
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
        Promise,
        setTimeout: timers.setTimeout,
        clearTimeout: timers.clearTimeout
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
    assert.equal(tabs[0].hidden, true);
    assert.equal(grids[1].hidden, false);
    assert.equal(grids[2].hidden, true);

    tabs[2].listeners.click();
    assert.equal(grids[1].hidden, true);
    assert.equal(grids[2].hidden, false);

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
    assert.equal(content.children[3].children[0].textContent, 'No recent movies.');
    assert.equal(content.children[5].children[0].textContent, 'Loading…');
    finishInProgress([]);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(content.children[5].children[0].textContent, 'No in-progress movies.');
});

function setupDashboard(getJSON, timers = fakeTimers()) {
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
            getJSON,
            getScaledImageUrl: () => ''
        },
        console,
        Date,
        Intl,
        Map,
        Set,
        Promise,
        setTimeout: timers.setTimeout,
        clearTimeout: timers.clearTimeout
    };
    vm.runInNewContext(dashboardSource.replace('export default function (view)', 'globalThis.init = function (view)'), context);
    context.init(view);
    view.listeners.viewshow();
    return { view, elements };
}

function dashboardResponse(route) {
    if (route.endsWith('/RecentlyWatched/Users')) return [{ Id: 'alice', Name: 'Alice' }];
    if (route.endsWith('/WatchHistoryVisibility')) return { Users: [], Rules: [] };
    if (route.endsWith('/WatchHistoryMenu')) return { Enabled: false };
    return [];
}

test('dashboard renders watched history before in-progress completes', async () => {
    let finishInProgress;
    const pending = new Promise(resolve => { finishInProgress = resolve; });
    const { elements } = setupDashboard(route => route.includes('/InProgress/') ? pending : dashboardResponse(route));
    await new Promise(resolve => setImmediate(resolve));
    const card = elements.get('#psHistoryUsers').children[0];
    card.open = true;
    card.listeners.toggle();
    await new Promise(resolve => setImmediate(resolve));
    const content = card.children[1];
    assert.equal(content.children[3].children[0].textContent, 'No recent movies.');
    assert.equal(content.children[5].children[0].textContent, 'Loading…');
    finishInProgress([]);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(content.children[5].children[0].textContent, 'No in-progress movies.');
});

test('menu updates the first tab as playback starts and stops and cancels polling on collapse', async () => {
    const timers = fakeTimers();
    let current = [];
    const { dialogElements } = setup(false, route => {
        if (route.endsWith('/RecentlyWatched/Users')) return [{ Id: 'alice', Name: 'Alice' }];
        if (route.includes('/CurrentlyWatching/')) return current;
        return [];
    }, timers);
    await new Promise(resolve => setImmediate(resolve));
    const card = dialogElements.get('.ps-wh-users').children[0];
    card.open = true;
    card.listeners.toggle();
    await new Promise(resolve => setImmediate(resolve));
    const content = card.children[1];
    const tabs = content.children[1].children;
    assert.equal(tabs[0].hidden, true);
    current = [{ ItemId: 'film', ItemName: 'Film', ItemType: 'Movie', CurrentlyWatching: true, ProgressPercent: 50 }];
    timers.run();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(tabs[0].hidden, false);
    assert.equal(tabs[0].textContent, 'Currently watching (1)');
    assert.equal(content.children[2].hidden, false);
    current = [];
    timers.run();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(tabs[0].hidden, true);
    assert.equal(content.children[3].hidden, false);
    card.open = false;
    card.listeners.toggle();
    assert.equal(timers.count(), 0);
});

test('dashboard updates active playback, respects tab selection, and skips hidden tabs in keyboard navigation', async () => {
    const timers = fakeTimers();
    let current = [
        { ItemId: 'film', ItemName: 'Film', ItemType: 'Movie', CurrentlyWatching: true },
        { ItemId: 'episode', ItemName: 'Episode', ItemType: 'Episode', CurrentlyWatching: true }
    ];
    const { view, elements } = setupDashboard(
        route => route.includes('/CurrentlyWatching/') ? current : dashboardResponse(route),
        timers);
    await new Promise(resolve => setImmediate(resolve));
    const card = elements.get('#psHistoryUsers').children[0];
    card.open = true;
    card.listeners.toggle();
    await new Promise(resolve => setImmediate(resolve));
    const content = card.children[1];
    const tabList = content.children[1];
    const tabs = tabList.children;
    assert.equal(tabs[0].hidden, false);
    assert.equal(tabs[0].textContent, 'Currently watching (2)');
    assert.equal(content.children[2].children.length, 2);
    tabs[2].listeners.click();
    timers.run();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(tabs[2].getAttribute('aria-selected'), 'true');
    current = [];
    timers.run();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(tabs[0].hidden, true);
    tabs[1].focus = () => {};
    tabList.listeners.keydown({ key: 'ArrowRight', target: tabs[4], preventDefault() {} });
    assert.equal(tabs[1].getAttribute('aria-selected'), 'true');
    view.listeners.viewhide();
    timers.run();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(timers.count(), 0);
});

test('menu reports playback errors and ignores responses from a closed card', async () => {
    const timers = fakeTimers();
    let response = () => Promise.reject({ status: 503 });
    const { dialogElements } = setup(false, route => {
        if (route.endsWith('/RecentlyWatched/Users')) return [{ Id: 'alice', Name: 'Alice' }];
        return route.includes('/CurrentlyWatching/') ? response() : [];
    }, timers);
    await new Promise(resolve => setImmediate(resolve));
    const card = dialogElements.get('.ps-wh-users').children[0];
    card.open = true;
    card.listeners.toggle();
    await new Promise(resolve => setImmediate(resolve));
    const content = card.children[1];
    const status = content.children.at(-1);
    assert.equal(status.hidden, false);
    assert.match(status.textContent, /HTTP 503/);
    let finish;
    const pending = new Promise(resolve => { finish = resolve; });
    response = () => pending;
    timers.run();
    card.open = false;
    card.listeners.toggle();
    finish([{ ItemId: 'film', ItemName: 'Film', ItemType: 'Movie' }]);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(content.children[1].children[0].hidden, true);
    assert.equal(timers.count(), 0);
});
