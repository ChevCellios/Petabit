const panel = document.getElementById('starlink-panel');
const button = document.getElementById('starlink-ping-button');
const canvas = document.getElementById('starlink-globe');
const ctx = canvas.getContext('2d');
const motion = document.getElementById('starlink-motion');
const speed = document.getElementById('starlink-speed');
if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) motion.checked = false;
let controller;
let generation = 0;
const strings = panel.dataset;
const status = document.getElementById('starlink-status');
let worker;
let frame;
let animation;
let pending = false;
let rotation = -.28;
let pointerX;
let zoom = 1;
let mappedLayers = [0,0,0,0];
const zoomInput = document.getElementById("starlink-zoom");
const layers = Array.from(panel.querySelectorAll("[data-satellite-layer]"));
const earthKm = 6371;
const continents = [
            [[72,-168],[70,-140],[60,-128],[55,-122],[49,-125],[43,-124],[34,-117],[23,-110],[17,-96],[25,-82],[30,-81],[43,-70],[51,-58],[59,-64],[66,-80],[73,-105],[72,-135],[72,-168]],
            [[12,-81],[8,-77],[-5,-81],[-18,-70],[-35,-72],[-55,-68],[-52,-55],[-34,-52],[-20,-40],[-5,-35],[7,-51],[12,-66],[12,-81]],
            [[71,-10],[65,5],[58,10],[51,3],[44,-9],[36,-6],[35,10],[43,18],[39,29],[47,40],[55,58],[60,80],[72,105],[70,140],[61,163],[50,143],[43,132],[35,126],[22,114],[10,105],[7,80],[21,72],[30,50],[38,36],[45,28],[55,22],[62,14],[71,-10]],
            [[36,-17],[37,10],[31,32],[15,43],[2,51],[-12,44],[-25,35],[-35,18],[-30,8],[-17,12],[-5,8],[5,-1],[15,-17],[28,-13],[36,-17]],
            [[-11,113],[-18,122],[-25,113],[-35,117],[-39,146],[-29,154],[-17,146],[-11,132],[-11,113]],
            [[83,-52],[77,-72],[68,-54],[60,-44],[64,-25],[76,-18],[83,-52]],
            [[-66,-180],[-70,-135],[-68,-90],[-73,-45],[-70,0],[-74,45],[-69,90],[-72,135],[-66,180]]
        ];
function resize() {
    const ratio = Math.min(devicePixelRatio || 1, 2);
    canvas.width = Math.round(canvas.clientWidth * ratio);
    canvas.height = Math.round(canvas.clientHeight * ratio);
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
}
function draw() {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    const cx = w / 2, cy = h / 2, radius = Math.min(w, h) * .385 * zoom;
    const light = document.body.classList.contains('light-mode');
    const cos = Math.cos(rotation), sin = Math.sin(rotation);
    const project = (lat, lon) => {
        const phi = lat * Math.PI / 180, lambda = lon * Math.PI / 180 + rotation;
        return { x: cx + radius * Math.cos(phi) * Math.sin(lambda),
            y: cy - radius * Math.sin(phi), front: Math.cos(phi) * Math.cos(lambda) >= 0 };
    };
    const line = (points, color, width = .7) => {
        ctx.strokeStyle = color;
        ctx.lineWidth = width;
        ctx.beginPath();
        let started = false;
        for (const [lat, lon] of points) {
            const p = project(lat, lon);
            if (!p.front) { started = false; continue; }
            if (!started) ctx.moveTo(p.x, p.y); else ctx.lineTo(p.x, p.y);
            started = true;
        }
        ctx.stroke();
    };
    ctx.clearRect(0, 0, w, h);
    const glow = ctx.createRadialGradient(cx, cy, radius * .6, cx, cy, radius * 1.3);
    glow.addColorStop(0, light ? '#b6e3f3' : '#15384f');
    glow.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = glow;
    ctx.fillRect(0, 0, w, h);
    // Rear satellites are visible only beyond Earth's limb.
    const dots = [];
    if (frame) {
        const dt = motion.checked && !document.hidden ? Math.min(.5, Math.max(0, (Date.now() - frame.sentAt) / 1000)) * frame.speed : 0;
        for (let i = 0; i < frame.count * 6; i += 6) {
            if (!layers[frame.statuses?.[i / 6] ?? 0].checked) continue;
            const p = frame.packed;
            const x = p[i] + p[i + 3] * dt, y = p[i + 1] + p[i + 4] * dt, z = p[i + 2] + p[i + 5] * dt;
            const depth = x * cos - y * sin;
            const dx = (y * cos + x * sin) / earthKm * radius, dy = -z / earthKm * radius;
            if (depth < 0 && dx * dx + dy * dy < radius * radius) continue;
            dots.push({ x: cx + dx, y: cy + dy, front: depth >= 0, status: frame.statuses?.[i / 6] ?? 0 });
        }
    }
    canvas.dataset.visibleCount = String(dots.length);
    if (frame) {
        const available = mappedLayers.some((count,index) => count > 0 && layers[index].checked);
        const empty = document.getElementById('starlink-empty');
        empty.hidden = available;
        empty.textContent = available ? '' : mappedLayers.some(count => count > 0) ? strings.allHidden : strings.layerEmpty;
    }
    const paintDots = front => {
        const colors = light ? ['#006c9c', '#9b6500', '#c63542', '#596b80'] : ['#8bdcff', '#ffd475', '#ff8491', '#b9c4d5'];
        for (let status = 0; status < colors.length; status++) {
            ctx.fillStyle = colors[status]; ctx.globalAlpha = front ? .82 : .35; ctx.beginPath();
            for (const p of dots) if (p.front === front && p.status === status) {
                const size = status === 2 ? 1.9 : .85;
                ctx.moveTo(p.x + size, p.y); ctx.arc(p.x, p.y, size, 0, Math.PI * 2);
            }
            ctx.fill();
        }
        ctx.globalAlpha = 1;
    };
    paintDots(false);
    ctx.fillStyle = light ? '#e5f6fa' : '#091b2b';
    ctx.beginPath(); ctx.arc(cx, cy, radius, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = light ? '#429ab7' : '#4594b5'; ctx.lineWidth = 1.5; ctx.stroke();
    for (let lat = -60; lat <= 60; lat += 30)
        line(Array.from({ length: 91 }, (_, i) => [lat, i * 4 - 180]), light ? '#add1db' : '#1d465d');
    for (let lon = -180; lon < 180; lon += 30)
        line(Array.from({ length: 61 }, (_, i) => [i * 3 - 90, lon]), light ? '#add1db' : '#1d465d');
    ctx.save(); ctx.beginPath(); ctx.arc(cx, cy, radius, 0, Math.PI * 2); ctx.clip();
    for (const points of continents) {
        ctx.fillStyle = light ? 'rgba(39,143,114,.23)' : 'rgba(58,191,150,.24)'; ctx.beginPath();
        let first = true;
        for (const [lat,lon] of points) {
            const p = project(lat,lon); if (!p.front) continue;
            if(first) ctx.moveTo(p.x,p.y); else ctx.lineTo(p.x,p.y); first=false;
        }
        ctx.closePath(); ctx.fill();
    }
    continents.forEach(points => line(points, light ? '#176f5c' : '#a3f1d1', 1.6)); ctx.restore();
    paintDots(true);
    if (motion.checked && !document.hidden && !panel.hidden && frame) animation = requestAnimationFrame(draw);
}
function run() {
    cancelAnimationFrame(animation);
    if (!worker) return;
    if (panel.hidden) { worker.postMessage({ type: 'stop' }); return; }
    if (document.hidden || !motion.checked) {
        worker.postMessage({ type: 'stop' });
    } else worker.postMessage({ type: 'start', speed: Number(speed.value) });
    status.textContent = motion.checked ? (speed.value === '1' ? strings.realtime : strings.simulation) : strings.paused;
    draw();
}
button.addEventListener('click', async () => {
    if (pending) return;
    pending = true;
    button.disabled = true;
    const currentGeneration = ++generation;
    controller = new AbortController();
    panel.hidden = false; button.setAttribute('aria-expanded', 'true');
    panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    status.textContent = strings.loading;
    const warning = document.getElementById('starlink-warning');
    warning.hidden = true;
    try {
        const response = await fetch('/Home/StarlinkData', { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(55000)]) });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const data = await response.json();
        if (generation !== currentGeneration || panel.hidden) return;
        const number = new Intl.NumberFormat(strings.locale);
        for (const [id, value] of [['operational', data.operationalCount], ['orbit', data.onOrbitCount], ['partial', data.partiallyOperationalCount], ['nonoperational', data.statusCoverageComplete ? data.nonOperationalCount : null]])
            document.getElementById(`starlink-${id}`).textContent = value === null ? '—' : number.format(value);
        const date = value => new Date(value).toLocaleString(strings.locale);
        document.getElementById('starlink-freshness').textContent = `${strings.updated}: ${date(data.retrievedAt)} · ${strings.epoch}: ${date(data.oldestEpoch)} – ${date(data.newestEpoch)}`;
        warning.textContent = [data.isStale ? strings.stale : '', data.refreshFailed ? strings.refreshFailed : ''].filter(Boolean).join(' ');
        if (data.persistenceFailed) warning.textContent += ` ${document.getElementById('iss-led-localization').dataset.persistence}`;
        if (!data.statusCoverageComplete) warning.textContent += ` ${strings.legacy}`;
        warning.hidden = !warning.textContent;
        worker?.terminate();
        cancelAnimationFrame(animation);
        frame = undefined;
        const workerUrl = new URL(strings.workerUrl, location.origin);
        workerUrl.searchParams.set('orbits', new URL(strings.orbitsUrl, location.origin).searchParams.get('v'));
        workerUrl.searchParams.set('clock', new URL(strings.clockUrl, location.origin).searchParams.get('v'));
        worker = new Worker(workerUrl, { type: 'module' });
        worker.onmessage = ({ data: next }) => {
            if (panel.hidden) return;
            frame = next;
            if (!next.count) status.textContent = strings.error;
            canvas.dataset.mappedCount = String(next.count);
            updateLayerCoverage(next, number);
            canvas.dataset.positionTime = String(next.timestamp);
            const nonoperationalMapped = next.statuses?.reduce((count, value) => count + (value === 2 ? 1 : 0), 0) ?? 0;
            const coverage = `${strings.mapped}: ${number.format(next.count)} / ${number.format(data.onOrbitCount)}`;
            document.getElementById('starlink-coverage').textContent = data.statusCoverageComplete
                ? `${coverage} · ${strings.nonoperational}: ${number.format(nonoperationalMapped)} / ${number.format(data.nonOperationalCount)}`
                : coverage;
            cancelAnimationFrame(animation);
            draw();

        };
        worker.onerror = () => { status.textContent = strings.error; worker?.terminate(); cancelAnimationFrame(animation); };
        resize();
        worker.postMessage({ type: motion.checked && !document.hidden ? 'start' : 'stop', elements: data.elements, speed: Number(speed.value) });
        status.textContent = motion.checked ? (speed.value === '1' ? strings.realtime : strings.simulation) : strings.paused;
    } catch {
        if (generation !== currentGeneration) return;
        status.textContent = strings.error;
        if (frame) { warning.textContent = strings.refreshFailed; warning.hidden = false; }
    } finally { if (generation === currentGeneration) { pending = false; button.disabled = false; } }
});
document.getElementById('starlink-close').addEventListener('click', () => {
    ++generation; controller?.abort(); pending = false; button.disabled = false;
    panel.hidden = true; button.setAttribute('aria-expanded', 'false'); run(); button.focus();
});
speed.addEventListener('change', run);
motion.addEventListener('change', run);
document.addEventListener('visibilitychange', run);
window.addEventListener('resize', () => { if (!panel.hidden) { resize(); cancelAnimationFrame(animation); draw(); } });
document.getElementById('toggle-dark-mode')?.addEventListener('click', () => { cancelAnimationFrame(animation); draw(); });
canvas.addEventListener('pointerdown', event => { pointerX = event.clientX; canvas.setPointerCapture(event.pointerId); });
canvas.addEventListener('pointermove', event => {
    if (pointerX === undefined) return;
    rotation += (event.clientX - pointerX) * .006;
    pointerX = event.clientX;
    cancelAnimationFrame(animation); draw();
});
canvas.addEventListener('pointerup', () => { pointerX = undefined; });
canvas.addEventListener('pointercancel', () => { pointerX = undefined; });
window.addEventListener('pagehide', () => { worker?.terminate(); cancelAnimationFrame(animation); });

function redrawView() { cancelAnimationFrame(animation); if (!panel.hidden) draw(); }
function setZoom(value) {
    zoom = Math.max(1, Math.min(3, Number(value) || 1));
    zoomInput.value = String(zoom);
    document.getElementById('starlink-zoom-value').value = zoom.toFixed(1) + '×';
    document.getElementById('starlink-zoom-out').disabled = zoom <= 1;
    document.getElementById('starlink-zoom-in').disabled = zoom >= 3;
    canvas.dataset.zoom = String(zoom);
    redrawView();
}
zoomInput.addEventListener('input', () => setZoom(zoomInput.value));
document.getElementById('starlink-zoom-in').addEventListener('click', () => setZoom(zoom + .2));
document.getElementById('starlink-zoom-out').addEventListener('click', () => setZoom(zoom - .2));
document.getElementById('starlink-view-reset').addEventListener('click', () => { rotation = -.28; setZoom(1); });
for (const layer of layers) layer.addEventListener('change', redrawView);
canvas.addEventListener('wheel', event => {
    const next = Math.max(1, Math.min(3, zoom - Math.sign(event.deltaY) * .1));
    if (next !== zoom) { event.preventDefault(); setZoom(next); }
}, { passive: false });
setZoom(1);

function updateLayerCoverage(next, number) {
    mappedLayers = [0,0,0,0];
    for (let index=0;index<next.count;index++) mappedLayers[next.statuses?.[index] ?? 0]++;
    for (let index=0;index<layers.length;index++) {
        const count=mappedLayers[index];
        document.querySelector('[data-layer-count="'+index+'"]').textContent = number.format(count);
        layers[index].disabled = count === 0;
        layers[index].parentElement.title = count === 0 ? strings.noPositions : '';
    }
}
