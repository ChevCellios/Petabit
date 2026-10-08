import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { OrbitalClock } from '../Petabit/wwwroot/js/orbital-clock.mjs';
function harness() {
    const imports = [], frames = [], timers = new Map();
    const self = { location: { href: 'https://petabit.example/js/starlink-worker.js?v=workerHash&orbits=orbitHash&clock=clockHash' },
        postMessage: frame => frames.push(frame) };
    const orbitModule = { createRecords: elements => elements, positionsAt: (records, timestamp) => ({
        timestamp, count: records.length, packed: new Float32Array(records.length * 6), statuses: new Uint8Array(records.length) }) };
    // Replace only the browser module-loader boundary; execute the real worker lifecycle.
    const source = fs.readFileSync(new URL('../Petabit/wwwroot/js/starlink-worker.js', import.meta.url), 'utf8')
        .replace('import(url.href)', 'loadModule(url.href)');
    vm.runInNewContext(source, { self, URL, Date, Float32Array, Uint8Array,
        loadModule: url => new Promise(resolve => imports.push({ url, resolve })),
        setTimeout: callback => { const id = timers.size + 1; timers.set(id, callback); return id; },
        clearTimeout: id => timers.delete(id) });
    return { self, imports, frames, timers, ready() { imports[0].resolve(orbitModule); imports[1].resolve({ OrbitalClock }); } };
}
test('first worker message is retained while versioned modules are loading', async () => {
    const h = harness();
    assert.equal(new URL(h.imports[0].url).searchParams.get('v'), 'orbitHash');
    assert.equal(new URL(h.imports[1].url).searchParams.get('v'), 'clockHash');
    const start = h.self.onmessage({ data: { type: 'start', elements: [{}], speed: 30 } });
    assert.equal(h.frames.length, 0);
    h.ready(); await start;
    assert.equal(h.frames[0].count, 1);
    assert.equal(h.frames[0].speed, 30);
    assert.equal(h.timers.size, 1);
});
test('pause during module loading wins over start without losing elements needed for resume', async () => {
    const h = harness();
    const start = h.self.onmessage({ data: { type: 'start', elements: [{}], speed: 30 } });
    const stop = h.self.onmessage({ data: { type: 'stop' } });
    h.ready(); await Promise.all([start, stop]);
    assert.equal(h.timers.size, 0);
    assert.equal(h.frames.length, 1);
    await h.self.onmessage({ data: { type: 'start', speed: 30 } });
    assert.equal(h.frames.at(-1).count, 1);
    assert.equal(h.timers.size, 1);
});
