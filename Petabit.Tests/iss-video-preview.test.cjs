const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function harness() {
    let now = 0;
    let nextTimer = 0;
    let requests = 0;
    const timers = new Map();
    const events = new Map();
    const players = [];
    const elements = new Map();
    const strings = { videoLoading: 'loading', videoPlaying: 'playing', videoEnded: 'finished',
        videoError: 'error', videoBlocked: 'blocked', videoTitle: 'ISS camera' };
    for (const id of ['iss-video-panel', 'iss-led-localization', 'iss-video-stage', 'iss-video-status'])
        elements.set(id, { hidden: true, dataset: id === 'iss-led-localization' ? strings : {},
            textContent: '', children: [], replaceChildren(...children) { this.children = children; } });
    const document = {
        hidden: false,
        getElementById: id => elements.get(id),
        addEventListener: (type, callback) => events.set(type, callback),
        createElement: () => ({}),
        head: { append() { throw new Error('API should already be available in this fixture'); } }
    };
    const window = {
        addEventListener: (type, callback) => events.set(type, callback),
        setTimeout: (callback, delay) => { const id = ++nextTimer; timers.set(id, { callback, at: now + delay }); return id; },
        clearTimeout: id => timers.delete(id),
        YT: {
            PlayerState: { PLAYING: 1, PAUSED: 2, BUFFERING: 3 },
            Player: function (iframe, options) {
                this.iframe = iframe;
                this.options = options;
                this.paused = 0;
                this.destroyed = false;
                this.mute = () => {};
                this.playVideo = () => {};
                this.pauseVideo = () => { this.paused++; };
                this.destroy = () => { this.destroyed = true; };
                this.state = state => options.events.onStateChange({ target: this, data: state });
                players.push(this);
            }
        }
    };
    const context = vm.createContext({ window, document, location: { origin: 'https://petabit.example' },
        performance: { now: () => now }, AbortSignal,
        fetch: async () => { requests++; return { ok: true, json: async () => ({ videoId: 'awQzjn72bI0' }) }; } });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../Petabit/wwwroot/js/iss-video-preview.js'), 'utf8'), context);
    events.get('DOMContentLoaded')();
    return {
        elements, players, document, events, requests: () => requests,
        start: () => events.get('iss-ping')(),
        tick(ms) {
            const until = now + ms;
            while (true) {
                const next = [...timers].filter(([, timer]) => timer.at <= until).sort((a, b) => a[1].at - b[1].at)[0];
                if (!next) break;
                timers.delete(next[0]); now = next[1].at; next[1].callback();
            }
            now = until;
        }
    };
}

test('does not contact YouTube/NASA or show the panel before manual Ping', () => {
    const app = harness();
    assert.equal(app.requests(), 0);
    assert.equal(app.elements.get('iss-video-panel').hidden, true);
    assert.equal(app.players.length, 0);
});

test('counts five seconds of playback, excluding loading and buffering', async () => {
    const app = harness();
    await app.start();
    const player = app.players[0];
    app.tick(4000); // Initial connection is not part of the preview.
    assert.equal(player.paused, 0);
    player.state(1);
    app.tick(2000);
    player.state(3);
    app.tick(3000); // Buffering is not part of the preview either.
    player.state(1);
    app.tick(2999);
    assert.equal(player.paused, 0);
    app.tick(1);
    assert.equal(player.paused, 1);
    assert.equal(app.elements.get('iss-video-panel').dataset.playback, 'finished');
});

test('a repeated Ping discards the previous timer and starts a new live preview', async () => {
    const app = harness();
    await app.start();
    const first = app.players[0];
    first.state(1);
    app.tick(4000);
    await app.start();
    assert.equal(first.destroyed, true);
    const second = app.players[1];
    second.state(1);
    app.tick(1000);
    assert.equal(second.paused, 0);
    app.tick(4000);
    assert.equal(second.paused, 1);
});

test('tab hiding pauses playback and does not consume the remaining preview', async () => {
    const app = harness();
    await app.start();
    const player = app.players[0];
    player.state(1);
    app.tick(1000);
    app.document.hidden = true;
    app.events.get('visibilitychange')();
    app.tick(10000);
    assert.equal(player.paused, 1);
    player.state(1);
    app.tick(4000);
    assert.equal(player.paused, 2);
});
