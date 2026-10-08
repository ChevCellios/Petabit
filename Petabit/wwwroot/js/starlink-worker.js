import { createRecords, positionsAt } from './starlink-orbits.mjs';
import { OrbitalClock } from './orbital-clock.mjs';
let records = [];
let timer;
const clock = new OrbitalClock();
function update() {
    const sentAt = Date.now();
    const frame = positionsAt(records, clock.now(sentAt));
    self.postMessage({ ...frame, sentAt, speed: clock.speed }, [frame.packed.buffer, frame.statuses.buffer]);
    if (clock.running) timer = setTimeout(update, clock.speed === 1 ? 1000 : 250);
}
self.onmessage = ({ data }) => {
    clearTimeout(timer);
    if (data.elements) records = createRecords(data.elements);
    clock.configure(data.type !== 'stop', data.speed);
    if (records.length) update();
};
