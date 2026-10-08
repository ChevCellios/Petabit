import test from 'node:test';
import assert from 'node:assert/strict';
import { OrbitalClock } from '../Petabit/wwwroot/js/orbital-clock.mjs';
test('30× simulation advances orbital time and pauses without jumping on resume', () => {
    const clock = new OrbitalClock(1000);
    clock.configure(true,30,1000);
    assert.equal(clock.now(2000),31000);
    clock.configure(false,30,2000);
    assert.equal(clock.now(6000),31000);
    clock.configure(true,30,6000);
    assert.equal(clock.now(7000),61000);
});
test('switching from simulation to real time returns to current wall time', () => {
    const clock = new OrbitalClock(1000);
    clock.configure(true,30,1000);
    clock.configure(true,1,5000);
    assert.equal(clock.now(6000),6000);
});
test('real-time resume catches up to wall time after a hidden tab', () => {
    const clock = new OrbitalClock(1000);
    clock.configure(true, 1, 1000);
    clock.configure(false, 1, 2000);
    assert.equal(clock.now(6000), 2000);
    clock.configure(true, 1, 6000);
    assert.equal(clock.now(7000), 7000);
});
