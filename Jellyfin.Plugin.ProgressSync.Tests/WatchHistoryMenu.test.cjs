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

function setup(compactLayout) {
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

test('history tabs switch panels and hidden panels have an explicit display rule', async () => {
    const { dialog, dialogElements } = setup(false);
    await new Promise(resolve => setImmediate(resolve));
    const card = dialogElements.get('.ps-wh-users').children[0];
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
