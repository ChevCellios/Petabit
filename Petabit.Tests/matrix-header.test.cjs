const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../Petabit/wwwroot/js/matrix-header.js'), 'utf8');

function harness({ reduced = false, hidden = false, hasCanvas = true, hasContext = true } = {}) {
    let now = 0;
    let nextFrame = 0;
    let paints = 0;
    let glyphs = 0;
    let light = false;
    let observedResize;
    let bounds = { width: 1200, height: 72 };
    const frames = new Map();
    const colors = [];
    const transforms = [];
    const events = new Map();
    function eventTarget(name, object = {}) {
        object.addEventListener = (type, callback) => events.set(`${name}:${type}`, callback);
        return object;
    }
    const navbar = { getBoundingClientRect: () => bounds };
    const context = {
        clearRect() { paints++; },
        setTransform(...args) { transforms.push(args); },
        fillText() { glyphs++; colors.push(this.fillStyle); }
    };
    const canvas = { getContext: () => hasContext ? context : null, closest: () => navbar };
    const toggle = eventTarget('toggle');
    const motion = eventTarget('motion', { matches: reduced });
    const document = eventTarget('document', {
        hidden,
        body: { classList: { contains: () => light } },
        getElementById: id => id === 'matrixCanvas' ? (hasCanvas ? canvas : null) : toggle
    });
    const window = eventTarget('window', {
        devicePixelRatio: 3,
        matchMedia: () => motion,
        requestAnimationFrame(callback) { const id = ++nextFrame; frames.set(id, callback); return id; },
        cancelAnimationFrame: id => frames.delete(id)
    });
    vm.runInNewContext(source, {
        document, window, performance: { now: () => now },
        Math: Object.assign(Object.create(Math), { random: () => 0.5 }),
        ResizeObserver: class {
            constructor(callback) { observedResize = callback; }
            observe(element) { assert.equal(element, navbar); }
        }
    });
    return {
        canvas, context, document, motion, colors, transforms,
        pending: () => frames.size,
        paints: () => paints,
        glyphs: () => glyphs,
        emit: (name, type) => events.get(`${name}:${type}`)?.(),
        setLight: value => { light = value; },
        resize(width, height) { bounds = { width, height }; observedResize(); },
        frame(time) {
            now = time;
            const callbacks = [...frames.values()];
            frames.clear();
            for (const callback of callbacks) callback(time);
            assert.ok(frames.size <= 1, 'at most one animation callback is pending');
        }
    };
}

test('caps drawing at 20 fps, bounds pixel density, and uses legible columns', () => {
    const h = harness();
    assert.equal(h.canvas.width, 2400);
    assert.equal(h.canvas.height, 144);
    assert.deepEqual(h.transforms[0], [2, 0, 0, 2, 0, 0]);
    assert.equal(h.context.font, '10px monospace');
    assert.ok(h.glyphs() > 0 && h.glyphs() <= 100 * 6);
    const initialPaints = h.paints();
    for (let time = 10; time <= 1000; time += 10) h.frame(time);
    assert.equal(h.paints() - initialPaints, 20);
    assert.equal(h.pending(), 1);
});

test('hidden tabs stop immediately and resume with a single loop', () => {
    const h = harness();
    h.document.hidden = true;
    h.emit('document', 'visibilitychange');
    const paints = h.paints();
    assert.equal(h.pending(), 0);
    h.frame(5000);
    assert.equal(h.paints(), paints);
    h.document.hidden = false;
    h.emit('document', 'visibilitychange');
    h.emit('document', 'visibilitychange');
    assert.equal(h.pending(), 1);
    h.frame(5050);
    assert.ok(h.paints() > paints);
});

test('reduced motion renders a static frame and reacts to preference changes', () => {
    const h = harness({ reduced: true });
    assert.equal(h.paints(), 1);
    assert.ok(h.glyphs() > 0);
    assert.equal(h.pending(), 0);
    h.motion.matches = false;
    h.emit('motion', 'change');
    assert.equal(h.pending(), 1);
    h.motion.matches = true;
    h.emit('motion', 'change');
    assert.equal(h.pending(), 0);
    const paints = h.paints();
    h.frame(1000);
    assert.equal(h.paints(), paints);
});

test('pagehide cannot be restarted by resize or theme events; pageshow restores eligibility', () => {
    const h = harness();
    h.emit('window', 'pagehide');
    const paints = h.paints();
    h.resize(600, 80);
    h.emit('toggle', 'click');
    h.emit('document', 'visibilitychange');
    assert.equal(h.pending(), 0);
    assert.equal(h.paints(), paints);
    h.emit('window', 'pageshow');
    assert.equal(h.pending(), 1);
    h.emit('window', 'pagehide');
    h.document.hidden = true;
    h.emit('window', 'pageshow');
    assert.equal(h.pending(), 0);
    h.document.hidden = false;
    h.motion.matches = true;
    h.emit('window', 'pageshow');
    assert.equal(h.pending(), 0);
    assert.ok(h.paints() > paints);
});

test('resize observer, window resize, and theme changes redraw without duplicate loops', () => {
    const h = harness();
    assert.ok(h.colors.some(color => color.startsWith('rgba(94,224,192,')));
    h.resize(600, 90);
    assert.equal(h.canvas.width, 1200);
    assert.equal(h.canvas.height, 180);
    h.emit('window', 'resize');
    h.setLight(true);
    h.colors.length = 0;
    h.emit('toggle', 'click');
    assert.ok(h.colors.some(color => color.startsWith('rgba(0,123,97,')));
    assert.equal(h.pending(), 1);
    h.resize(0, 0);
    assert.equal(h.pending(), 0);
    h.resize(600, 90);
    assert.equal(h.pending(), 1);
});

test('missing canvas or drawing context exits safely', () => {
    assert.equal(harness({ hasCanvas: false }).pending(), 0);
    assert.equal(harness({ hasContext: false }).pending(), 0);
    assert.equal(harness({ hidden: true }).pending(), 0);
});
