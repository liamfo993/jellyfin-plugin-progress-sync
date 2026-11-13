const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const html = fs.readFileSync(path.join(__dirname, '../Jellyfin.Plugin.ProgressSync/Configuration/configPage.html'), 'utf8');
const script = html.match(/<script type="text\/javascript">([\s\S]*?)<\/script>/)[1];

function openPage(getJSON) {
    const elements = new Map();
    const listeners = new Map();
    function element() {
        return {
            value: '',
            options: [],
            dataset: {},
            classList: { add() {}, remove() {}, toggle() {} },
            addEventListener() {},
            setAttribute() {},
            appendChild() {},
            replaceChildren(...children) { this.children = children; },
            closest() { return element(); }
        };
    }

    const page = element();
    page.querySelector = function (selector) {
        if (!elements.has(selector)) {
            elements.set(selector, element());
        }
        return elements.get(selector);
    };
    page.querySelectorAll = () => [];
    page.addEventListener = (name, callback) => listeners.set(name, callback);
    const context = {
        document: {
            querySelector: () => page,
            createElement: element
        },
        ApiClient: {
            getUrl: route => 'https://jellyfin.example/' + route,
            getJSON
        },
        Dashboard: {
            showLoadingMsg() { throw new Error('Global loading overlay must not be used'); }
        },
        URL,
        console
    };
    vm.runInNewContext(script, context);
    listeners.get('pageshow')();
    return elements;
}

test('page starts browsing and loading settings without a blocking overlay', async () => {
    const requests = [];
    const pending = new Promise(() => {});
    const elements = openPage(url => {
        requests.push(url);
        return pending;
    });

    assert.equal(requests.length, 3);
    assert.ok(requests.some(url => url.includes('/Admin/Browse')));
    assert.ok(requests.some(url => url.includes('/Admin/Overview')));
    assert.ok(requests.some(url => url.includes('/Admin/GlobalSettings')));
    assert.equal(elements.get('#psSeriesItemsField').hidden, true);
    assert.equal(elements.get('#psMusicItemsField').hidden, true);
    assert.equal(elements.get('#psFtStatus').children[0].textContent, 'Loading sync settings...');
});

test('page reports errors when browsing or loading settings fails', async () => {
    const elements = openPage(() => Promise.reject({ status: 503 }));
    await new Promise(resolve => setImmediate(resolve));

    assert.match(elements.get('#psSetupStatus').textContent, /Could not browse.*HTTP 503/);
    assert.match(elements.get('#psFtStatus').children[0].textContent, /Could not load status.*HTTP 503/);
});
