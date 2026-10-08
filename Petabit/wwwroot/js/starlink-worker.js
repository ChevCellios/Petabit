import { createRecords, positionsAt } from './starlink-orbits.mjs';
let records = [];
let timer;
function update() {
    const frame = positionsAt(records, Date.now());
    self.postMessage(frame, [frame.packed.buffer]);
    timer = setTimeout(update, 1000);
}
self.onmessage = ({ data }) => {
    clearTimeout(timer);
    if (data.type === 'stop') return;
    if (data.elements) records = createRecords(data.elements);
    if (records.length) update();
};
