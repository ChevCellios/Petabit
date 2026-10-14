const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function setup() {
    const events = {}, keys = {}, panels = {};
    const document = { activeElement: { closest: () => null },
        getElementById: id => panels[id],
        querySelector: () => Object.values(panels).find(panel => !panel.hidden),
        addEventListener: (name, handler) => { keys[name] = handler; } };
    for (const name of ['iss', 'starlink']) {
        const panel = { hidden: true, focus() { document.activeElement = { closest: () => panel }; },
            querySelector: () => ({ click() { panel.hidden = true; document.activeElement = { closest: () => null }; } }) };
        panels[name + '-panel'] = panel;
    }
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../Petabit/wwwroot/js/tracker-accessibility.js'), 'utf8'), {
        document, window: { addEventListener: (name, handler) => { events[name] = handler; } }
    });
    return { document, panels, open(name) { panels[name+'-panel'].hidden=false;events[name+'-panel-open'](); },
        escape(prevented=false) { const event={key:'Escape',defaultPrevented:prevented,preventDefault(){this.defaultPrevented=true;}};keys.keydown(event);return event.defaultPrevented; } };
}

test('Escape closes the focused tracker then the other remaining open tracker', () => {
    const h=setup();h.open('iss');h.open('starlink');
    assert.equal(h.escape(),true);assert.equal(h.panels['starlink-panel'].hidden,true);assert.equal(h.panels['iss-panel'].hidden,false);
    assert.equal(h.escape(),true);assert.equal(h.panels['iss-panel'].hidden,true);
    assert.equal(h.escape(),false);
});

test('focused tracker wins over last opened and handled Escape events are preserved', () => {
    const h=setup();h.open('iss');h.open('starlink');h.panels['iss-panel'].focus();
    h.escape(true);assert.equal(h.panels['iss-panel'].hidden,false);
    h.escape();assert.equal(h.panels['iss-panel'].hidden,true);assert.equal(h.panels['starlink-panel'].hidden,false);
});
