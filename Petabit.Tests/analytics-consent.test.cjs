const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../Petabit/wwwroot/js/site.js'), 'utf8');

function harness({saved=null,blocked=false,writeBlocked=blocked,cookiesBlocked=false,measurementId='G-FIXTURE'}={}) {
    const events = new Map(), scripts = [], deletedCookies = [], storage = new Map();
    if(saved!==null)storage.set('petabit-analytics-consent',saved);
    let failWrite=writeBlocked,submitted=0,scroll;
    const element=id=>({hidden:true,addEventListener(type,callback){events.set(`${id}:${type}`,callback);}});
    const banner=element('banner');banner.focus=()=>{};banner.scrollIntoView=options=>{scroll=options;};
    const status=element('status'),accept=element('accept'),reject=element('reject'),manage=element('manage'),footer=element('footer'),culture=element('culture');
    culture.form={requestSubmit:()=>submitted++};
    const nodes={'analytics-consent':banner,'analytics-storage-status':status,'analytics-accept':accept,'analytics-reject':reject,'culture-selector':culture};
    const main={setAttribute(){},focus(){this.focused=true;}};
    const document={body:{dataset:{analyticsId:measurementId}},head:{appendChild:script=>scripts.push(script)},
        addEventListener:(type,callback)=>events.set(type,callback),getElementById:id=>nodes[id],
        querySelector:selector=>selector==='main'?main:scripts[0],querySelectorAll:()=>[manage,footer],createElement:()=>({dataset:{}})};
    for(const control of [manage,footer]){control.isConnected=true;control.focus=()=>{control.focused=true;};}
    Object.defineProperty(document,'cookie',{get(){if(cookiesBlocked)throw Error('Blocked cookies');return '_ga=test; _ga_FIXTURE=test; unrelated=keep';},set(value){if(cookiesBlocked)throw Error('Blocked cookies');deletedCookies.push(value);}});
    const window={matchMedia:()=>({matches:true})};
    const localStorage={getItem:key=>{if(blocked)throw Error('Blocked storage');return storage.get(key)??null;},
        setItem:(key,value)=>{if(failWrite)throw Error('Blocked storage');storage.set(key,value);},
        removeItem:key=>{if(failWrite)throw Error('Blocked storage');storage.delete(key);}};
    vm.runInNewContext(source,{document,window,localStorage,Date,encodeURIComponent});
    events.get('DOMContentLoaded')();
    return {window,banner,status,scripts,deletedCookies,storage,main,manage,footer,click:id=>events.get(`${id}:click`)(),
        changeLanguage:()=>events.get('culture:change')(),submitted:()=>submitted,scroll:()=>scroll,
        allowWrite:()=>{failWrite=false;}};
}
test('analytics loads only after explicit saved acceptance and never inserts duplicate scripts',()=>{
    const h=harness();assert.equal(h.banner.hidden,false);assert.equal(h.scripts.length,0);
    assert.equal(h.window['ga-disable-G-FIXTURE'],true);
    h.click('accept');assert.equal(h.storage.get('petabit-analytics-consent'),'accepted');
    assert.equal(h.banner.hidden,true);assert.equal(h.scripts.length,1);assert.equal(h.window['ga-disable-G-FIXTURE'],false);
    h.click('accept');assert.equal(h.scripts.length,1);
});
test('stored rejection and unknown preferences keep analytics disabled',()=>{
    const rejected=harness({saved:'rejected'});assert.equal(rejected.banner.hidden,true);assert.equal(rejected.scripts.length,0);
    const invalid=harness({saved:'unexpected'});assert.equal(invalid.banner.hidden,false);assert.equal(invalid.scripts.length,0);
});
test('blocked browser storage fails closed without breaking language changes or rejection',()=>{
    const h=harness({blocked:true,cookiesBlocked:true});
    h.changeLanguage();assert.equal(h.submitted(),1);
    h.click('accept');assert.equal(h.scripts.length,0);assert.equal(h.banner.hidden,false);assert.equal(h.status.hidden,false);
    assert.equal(h.window['ga-disable-G-FIXTURE'],true);
    h.click('reject');assert.equal(h.banner.hidden,true);assert.equal(h.scripts.length,0);
});
test('failed consent persistence stays off and can recover after browser storage is available',()=>{
    const h=harness({writeBlocked:true});h.click('accept');assert.equal(h.scripts.length,0);assert.equal(h.status.hidden,false);
    h.allowWrite();h.click('accept');assert.equal(h.scripts.length,1);assert.equal(h.status.hidden,true);
});
test('privacy and footer controls reopen consent and revocation disables tracking and clears analytics cookies',()=>{
    const h=harness({saved:'accepted'});assert.equal(h.scripts.length,1);
    for(const control of ['manage','footer']){h.click(control);assert.equal(h.banner.hidden,false);}
    assert.equal(h.scroll().behavior,'auto');
    h.click('reject');assert.equal(h.window['ga-disable-G-FIXTURE'],true);assert.equal(h.storage.get('petabit-analytics-consent'),'rejected');
    assert.equal(h.deletedCookies.length,2);assert.ok(h.deletedCookies.every(cookie=>cookie.startsWith('_ga')));
    assert.equal(h.banner.hidden,true);
});
test('missing analytics configuration never loads external analytics',()=>{
    const h=harness({measurementId:''});assert.equal(h.banner.hidden,true);h.click('accept');assert.equal(h.scripts.length,0);
});

test('closing consent restores its opener, or main content for the initial banner',()=>{
    const h=harness();h.click('reject');assert.equal(h.main.focused,true);
    h.click('footer');h.click('reject');assert.equal(h.footer.focused,true);
    h.click('manage');h.click('accept');assert.equal(h.manage.focused,true);
});
