function versionedModule(path, parameter) {
    const url = new URL(path, self.location.href);
    const version = new URL(self.location.href).searchParams.get(parameter);
    if (version) url.searchParams.set('v', version);
    return import(url.href);
}
const modules = Promise.all([
    versionedModule('./starlink-orbits.mjs', 'orbits'), versionedModule('./orbital-clock.mjs', 'clock')
]);
let records = [];
let timer;
let clock;
let orbitModule;
let generation = 0;
let pendingElements;
function update() {
    const sentAt = Date.now();
    const frame = orbitModule.positionsAt(records, clock.now(sentAt));
    self.postMessage({ ...frame, sentAt, speed: clock.speed }, [frame.packed.buffer, frame.statuses.buffer]);
    if (clock.running) timer = setTimeout(update, clock.speed === 1 ? 1000 : 250);
}
self.onmessage = async ({ data }) => {
    const currentGeneration = ++generation;
    if (data.elements) pendingElements = data.elements;
    const [orbits, { OrbitalClock }] = await modules;
    if (currentGeneration !== generation) return;
    orbitModule = orbits;
    clock ??= new OrbitalClock();
    clearTimeout(timer);
    if (pendingElements) { records = orbitModule.createRecords(pendingElements); pendingElements = undefined; }
    clock.configure(data.type !== 'stop', data.speed);
    if (records.length) update();
};
