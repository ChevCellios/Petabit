import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createIllustrativeOrbit } from '../Petabit/wwwroot/js/iss-orbit.mjs';

const radians = Math.PI / 180;
const earthRotation = 2 * Math.PI / 86164;
const angularSpeed = 2 * Math.PI / (92.7 * 60);
const now = Date.parse('2026-10-12T12:00:00Z');
function sample(phase, seconds = 0, node = 0) {
    const angle = phase + angularSpeed * seconds;
    const latitude = Math.asin(Math.sin(51.6 * radians) * Math.sin(angle)) / radians;
    const longitude = (Math.atan2(Math.cos(51.6 * radians) * Math.sin(angle), Math.cos(angle))
        + node * radians - earthRotation * seconds) / radians;
    return { latitude, longitude: ((longitude + 540) % 360 + 360) % 360 - 180,
        positionUpdatedAt: new Date(now + seconds * 1000).toISOString() };
}
function close(actual, expected) {
    assert.ok(Math.abs(actual.latitude - expected.latitude) < 1e-7);
    assert.ok(Math.abs(((actual.longitude - expected.longitude + 540) % 360) - 180) < 1e-7);
}

test('orbit follows measured ascending and descending passes in both hemispheres', () => {
    for (const phase of [-2.7, -0.5, 0.5, 2.7]) {
        const current = sample(phase), previous = sample(phase, -30);
        const orbit = createIllustrativeOrbit({ ...current, previousPosition: previous });
        assert.ok(orbit);
        close(orbit(0), current);
        close(orbit(-angularSpeed * 30), previous);
        close(orbit(angularSpeed * 10), sample(phase, 10));
        assert.equal(Math.sign(orbit(0.01).latitude - current.latitude), Math.sign(Math.cos(phase)));
    }
});

test('orientation stays continuous across the dateline and at the northern/southern turning points', () => {
    for (const phase of [0.2, Math.PI / 2 + 0.002, -Math.PI / 2 + 0.002]) {
        const current = sample(phase, 0, 174), previous = sample(phase, -30, 174);
        const orbit = createIllustrativeOrbit({ ...current, previousPosition: previous });
        close(orbit(angularSpeed * 20), sample(phase, 20, 174));
        for (let angle = -Math.PI; angle <= Math.PI; angle += 0.02) {
            const point = orbit(angle);
            assert.ok(Number.isFinite(point.latitude) && Math.abs(point.latitude) <= 90);
            assert.ok(Number.isFinite(point.longitude) && Math.abs(point.longitude) <= 180);
        }
    }
});

test('missing, repeated, stale or implausible samples never invent an orbital direction', () => {
    const current = sample(0.5), previous = sample(0.5, -30);
    assert.equal(createIllustrativeOrbit(current), null);
    for (const invalid of [current, { ...previous, positionUpdatedAt: 'invalid' },
        sample(0.5, -121), sample(0.5, 30), { ...previous, latitude: NaN },
        { ...previous, longitude: 190 }, { ...current, positionUpdatedAt: previous.positionUpdatedAt },
        { ...previous, latitude: -40 }]) {
        assert.equal(createIllustrativeOrbit({ ...current, previousPosition: invalid }), null);
    }
});
