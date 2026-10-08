const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function harness() {
    const events = new Map(), listeners = new Map(), requests = [];
    const elements = new Map();
    for (const id of ['station-dashboard','iss-led-localization','iss-ping-button','iss-panel','iss-close','iss-led-display','iss-led-message','iss-sound-enabled']) {
        elements.set(id, { hidden: true, disabled: false, checked: false, dataset: { loading: 'loading', refresh: 'Ping ISS' },
            textContent: '', addEventListener: (name, fn) => listeners.set(`${id}:${name}`, fn),
            setAttribute() {}, scrollIntoView() {}, focus() { this.focused = true; }, classList: { add() {} } });
    }
    const document = { hidden: false, getElementById: id => elements.get(id),
        addEventListener: (name, fn) => events.set(name, fn), querySelector: () => ({ prepend() {} }) };
    const window = { dispatchEvent() {}, setInterval: fn => { events.set('interval', fn); } };
    vm.runInNewContext(fs.readFileSync(require('node:path').join(__dirname,'../Petabit/wwwroot/js/station-dashboard.js'),'utf8'), {
        document, window, Event, AbortController, AbortSignal, fetch: (url, options) => new Promise(resolve => requests.push({ url, signal: options.signal, resolve }))
    });
    events.get('DOMContentLoaded')();
    return { elements, events, requests, click: id => listeners.get(`${id}:click`)() };
}
test('hidden ISS panel performs no initial, interval or visibility data requests', () => {
    const h = harness();
    h.events.get('interval')(); h.events.get('visibilitychange')();
    assert.equal(h.requests.length, 0);
    h.click('iss-ping-button');
    assert.equal(h.requests.length, 2);
    assert.equal(h.elements.get('iss-panel').hidden, false);
});
test('closing pending ISS cancels visitor requests and allows immediate reopening; late responses are ignored', async () => {
    const h = harness();
    h.click('iss-ping-button');
    h.click('iss-close');
    assert.equal(h.elements.get('iss-panel').hidden, true);
    assert.equal(h.elements.get('iss-ping-button').disabled, false);
    assert.equal(h.elements.get('iss-ping-button').focused, true);
    assert.ok(h.requests.every(r => r.signal.aborted));
    for (const r of h.requests) r.resolve({ ok: false, status: 503 });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(h.elements.get('iss-panel').hidden, true);
    h.click('iss-ping-button');
    assert.equal(h.requests.length, 4);
    assert.equal(h.elements.get('iss-panel').hidden, false);
});
