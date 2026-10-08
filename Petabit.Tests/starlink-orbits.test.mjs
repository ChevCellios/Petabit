import test from 'node:test';
import assert from 'node:assert/strict';
import { createRecords, positionsAt } from '../Petabit/wwwroot/js/starlink-orbits.mjs';
const omm = { OBJECT_NAME: 'STARLINK-1008', OBJECT_ID: '2019-074B', NORAD_CAT_ID: 44714,
    EPOCH: '2026-10-04T22:36:12.73824', MEAN_MOTION: 15.66040205, ECCENTRICITY: .0002923,
    INCLINATION: 53.1448, RA_OF_ASC_NODE: 256.2279, ARG_OF_PERICENTER: 92.7295,
    MEAN_ANOMALY: 267.4056, EPHEMERIS_TYPE: 0, CLASSIFICATION_TYPE: 'U',
    ELEMENT_SET_NO: 999, REV_AT_EPOCH: 38133, BSTAR: .00047264149,
    MEAN_MOTION_DOT: .00049034, MEAN_MOTION_DDOT: 0 };
test('OMM and six-digit catalog IDs give a valid Earth-fixed orbital position', () => {
    const records = createRecords([{ ...omm, NORAD_CAT_ID: 100800 }]);
    const frame = positionsAt(records, Date.parse(omm.EPOCH + 'Z'));
    assert.equal(frame.count, 1);
    assert.ok([...frame.packed].every(Number.isFinite));
    const height = Math.hypot(...frame.packed.slice(0, 3)) - 6371;
    assert.ok(height > 300 && height < 700);
});
test('real-time positions move and Earth-fixed velocity predicts the next second', () => {
    const records = createRecords([omm]);
    const now = Date.parse(omm.EPOCH + 'Z');
    const a = positionsAt(records, now).packed;
    const b = positionsAt(records, now + 1000).packed;
    const displacement = Math.hypot(...[0, 1, 2].map(i => b[i] - a[i]));
    assert.ok(displacement > 6 && displacement < 9);
    const error = Math.hypot(...[0, 1, 2].map(i => b[i] - (a[i] + a[i + 3])));
    assert.ok(error < .02, `Interpolation error: ${error} km`);
});
test('unpropagatable elements are omitted rather than displaying invented locations', () => {
    const records = createRecords([{ ...omm, ECCENTRICITY: 2 }]);
    assert.equal(positionsAt(records, Date.parse(omm.EPOCH + 'Z')).count, 0);
});
