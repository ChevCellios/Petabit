import { json2satrec } from '../lib/satellite-js/dist/io.js';
import { propagate, gstime } from '../lib/satellite-js/dist/propagation.js';
import { eciToEcf } from '../lib/satellite-js/dist/transforms.js';

export function createRecords(elements) {
    return elements.map(element => {
        try { return json2satrec(element); } catch { return null; }
    }).filter(Boolean);
}

// Earth-fixed km and km/s. Main-thread interpolation keeps the animation smooth
// while SGP4 runs once a second in a worker, including Earth's own rotation.
export function positionsAt(records, timestamp) {
    const time = new Date(timestamp);
    const sidereal = gstime(time);
    const packed = new Float32Array(records.length * 6);
    let count = 0;
    for (const record of records) {
        const state = propagate(record, time);
        if (!state?.position || !state.velocity) continue;
        const p = eciToEcf(state.position, sidereal);
        const v = eciToEcf(state.velocity, sidereal);
        if (![p.x, p.y, p.z, v.x, v.y, v.z].every(Number.isFinite)
            || Math.hypot(p.x, p.y, p.z) < 6371) continue;
        packed.set([p.x, p.y, p.z, v.x + 7.292115e-5 * p.y,
            v.y - 7.292115e-5 * p.x, v.z], count * 6);
        count++;
    }
    return { timestamp, count, packed: packed.subarray(0, count * 6) };
}
