const panel = document.getElementById('starlink-panel');
const button = document.getElementById('starlink-ping-button');
const canvas = document.getElementById('starlink-globe');
const ctx = canvas.getContext('2d');
const motion = document.getElementById('starlink-motion');
const strings = panel.dataset;
const status = document.getElementById('starlink-status');
let worker;
let frame;
let animation;
let pending = false;
let rotation = -.28;
let frozenAt;
let pointerX;
const earthKm = 6371;
const continents = [
    [[72,-168],[60,-128],[49,-125],[34,-117],[17,-96],[25,-82],[43,-70],[59,-64],[73,-105],[72,-168]],
    [[12,-81],[-5,-81],[-35,-72],[-55,-68],[-34,-52],[-5,-35],[12,-66],[12,-81]],
    [[71,-10],[51,3],[36,-6],[35,10],[47,40],[60,80],[72,105],[61,163],[35,126],[10,105],[7,80],[30,50],[45,28],[71,-10]],
    [[36,-17],[37,10],[31,32],[2,51],[-25,35],[-35,18],[-17,12],[5,-1],[15,-17],[36,-17]],
    [[-11,113],[-25,113],[-35,117],[-39,146],[-17,146],[-11,132],[-11,113]],
    [[83,-52],[68,-54],[60,-44],[64,-25],[76,-18],[83,-52]]
];
function resize() {
    const ratio = Math.min(devicePixelRatio || 1, 2);
    canvas.width = Math.round(canvas.clientWidth * ratio);
    canvas.height = Math.round(canvas.clientHeight * ratio);
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
}
function draw() {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    const cx = w / 2, cy = h / 2, radius = Math.min(w, h) * .385;
    const light = document.body.classList.contains('light-mode');
    const cos = Math.cos(rotation), sin = Math.sin(rotation);
    const project = (lat, lon) => {
        const phi = lat * Math.PI / 180, lambda = lon * Math.PI / 180 + rotation;
        return { x: cx + radius * Math.cos(phi) * Math.sin(lambda),
            y: cy - radius * Math.sin(phi), front: Math.cos(phi) * Math.cos(lambda) >= 0 };
    };
    const line = (points, color) => {
        ctx.strokeStyle = color;
        ctx.lineWidth = .7;
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
        const dt = Math.min(2, Math.max(0, ((motion.checked ? Date.now() : frozenAt) - frame.timestamp) / 1000));
        for (let i = 0; i < frame.count * 6; i += 6) {
            const p = frame.packed;
            const x = p[i] + p[i + 3] * dt, y = p[i + 1] + p[i + 4] * dt, z = p[i + 2] + p[i + 5] * dt;
            const depth = x * cos - y * sin;
            const dx = (y * cos + x * sin) / earthKm * radius, dy = -z / earthKm * radius;
            if (depth < 0 && dx * dx + dy * dy < radius * radius) continue;
            dots.push({ x: cx + dx, y: cy + dy, front: depth >= 0 });
        }
    }
    const paintDots = front => {
        ctx.fillStyle = front ? (light ? '#006799' : '#8ad9ff') : (light ? '#87aabd' : '#355d7d');
        ctx.beginPath();
        for (const p of dots) if (p.front === front) { ctx.moveTo(p.x + 1.15, p.y); ctx.arc(p.x, p.y, 1.15, 0, Math.PI * 2); }
        ctx.fill();
    };
    paintDots(false);
    ctx.fillStyle = light ? '#e5f6fa' : '#091b2b';
    ctx.beginPath(); ctx.arc(cx, cy, radius, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = light ? '#429ab7' : '#4594b5'; ctx.lineWidth = 1.5; ctx.stroke();
    for (let lat = -60; lat <= 60; lat += 30)
        line(Array.from({ length: 91 }, (_, i) => [lat, i * 4 - 180]), light ? '#add1db' : '#1d465d');
    for (let lon = -180; lon < 180; lon += 30)
        line(Array.from({ length: 61 }, (_, i) => [i * 3 - 90, lon]), light ? '#add1db' : '#1d465d');
    continents.forEach(points => line(points, light ? '#418d88' : '#61ada6'));
    paintDots(true);
    if (motion.checked && !document.hidden && !panel.hidden && frame) animation = requestAnimationFrame(draw);
}
function run() {
    cancelAnimationFrame(animation);
    if (!worker || panel.hidden) return;
    if (document.hidden || !motion.checked) {
        frozenAt = Date.now();
        worker.postMessage({ type: 'stop' });
    } else worker.postMessage({ type: 'start' });
    status.textContent = motion.checked ? strings.motion : strings.paused;
    draw();
}
button.addEventListener('click', async () => {
    if (pending) return;
    pending = true;
    button.disabled = true;
    panel.hidden = false;
    panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    status.textContent = strings.loading;
    const warning = document.getElementById('starlink-warning');
    warning.hidden = true;
    try {
        const response = await fetch('/Home/StarlinkData', { cache: 'no-store', signal: AbortSignal.timeout(55000) });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const data = await response.json();
        const number = new Intl.NumberFormat(strings.locale);
        for (const [id, value] of [['operational', data.operationalCount], ['orbit', data.onOrbitCount], ['partial', data.partiallyOperationalCount]])
            document.getElementById(`starlink-${id}`).textContent = number.format(value);
        const date = value => new Date(value).toLocaleString(strings.locale);
        document.getElementById('starlink-freshness').textContent = `${strings.updated}: ${date(data.retrievedAt)} · ${strings.epoch}: ${date(data.oldestEpoch)} – ${date(data.newestEpoch)}`;
        warning.textContent = data.isStale || data.refreshFailed ? strings.stale : '';
        if (data.persistenceFailed) warning.textContent += ` ${document.getElementById('iss-led-localization').dataset.persistence}`;
        warning.hidden = !warning.textContent;
        worker?.terminate();
        cancelAnimationFrame(animation);
        frame = undefined;
        worker = new Worker(new URL('./starlink-worker.js', import.meta.url), { type: 'module' });
        worker.onmessage = ({ data: next }) => {
            frame = next;
            if (!next.count) status.textContent = strings.error;
            frozenAt = next.timestamp;
            canvas.dataset.mappedCount = String(next.count);
            canvas.dataset.positionTime = String(next.timestamp);
            document.getElementById('starlink-coverage').textContent = `${strings.mapped}: ${number.format(next.count)} / ${number.format(data.operationalCount)}`;
            cancelAnimationFrame(animation);
            draw();
            if (!motion.checked || document.hidden) worker.postMessage({ type: 'stop' });
        };
        worker.onerror = () => { status.textContent = strings.error; worker?.terminate(); cancelAnimationFrame(animation); };
        resize();
        worker.postMessage({ type: 'start', elements: data.elements });
        status.textContent = motion.checked ? strings.motion : strings.paused;
    } catch {
        status.textContent = strings.error;
        if (frame) { warning.textContent = strings.stale; warning.hidden = false; }
    } finally { pending = false; button.disabled = false; }
});
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
