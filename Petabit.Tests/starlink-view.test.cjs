const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
function setup(){
 const elements=new Map();
 const element=id=>{if(!elements.has(id)) elements.set(id,{dataset:{},checked:true,value:'1',hidden:true,clientWidth:800,clientHeight:500,events:{},attributes:{},style:{setProperty(){}},addEventListener(type,fn){this.events[type]=fn;},setAttribute(key,value){this.attributes[key]=value;},querySelector(selector){return element(id+' '+selector);},scrollIntoView(){},focus(){}});return elements.get(id);};
 const layers=Array.from({length:4},(_,i)=>{ const layer=element('layer'+i); layer.parentElement={}; return layer; });
 const context2d=new Proxy({createRadialGradient(){return {addColorStop(){}};}},{get:(obj,key)=>key in obj?obj[key]:()=>{}});
 element('starlink-globe').getContext=()=>context2d;
 element('starlink-panel').querySelectorAll=()=>layers;
 Object.assign(element('starlink-panel').dataset,{locale:'en',loading:'Loading',calculating:'Calculating',ready:'Ready',error:'Unavailable',fresh:'Current',aging:'Older',age:'Age',ageUnknown:'Unknown',workerUrl:'/worker.js',orbitsUrl:'/orbits.js?v=1',clockUrl:'/clock.js?v=1'});
 Object.assign(element('starlink-ping-sound'),{currentTime:0,plays:0,pauses:0,play(){this.plays++;return Promise.resolve();},pause(){this.pauses++;}});
 const workers=[];
 class FakeWorker { constructor(){this.messages=[];workers.push(this);} postMessage(data){this.messages.push(data);} terminate(){this.terminated=true;} }
 const documentEvents={},windowEvents={};
 const sandbox=vm.createContext({document:{getElementById:element,querySelector:selector=>element(selector),hidden:false,body:{classList:{contains:()=>false}},addEventListener(type,fn){documentEvents[type]=fn;}},window:{matchMedia:()=>({matches:true}),addEventListener(type,fn){windowEvents[type]=fn;}},devicePixelRatio:1,cancelAnimationFrame(){},requestAnimationFrame(){return 1;},console,Date,Math,Number,String,Array,URL,AbortController,AbortSignal,Worker:FakeWorker,location:{origin:'https://example.invalid'},fetch:async()=>{throw Error('Unavailable');}});
 vm.runInContext(fs.readFileSync('Petabit/wwwroot/js/starlink-dashboard.js','utf8'),sandbox);
 return {sandbox,element,layers,workers,documentEvents,windowEvents};
}
test('zoom clamps to usable limits and reset restores orientation without changing layers',()=>{
 const {sandbox,element,layers}=setup();
 vm.runInContext('setZoom(99)',sandbox);assert.equal(element('starlink-globe').dataset.zoom,'3');assert.equal(element('starlink-zoom-in').disabled,true);
 vm.runInContext('setZoom(-5)',sandbox);assert.equal(element('starlink-globe').dataset.zoom,'1');assert.equal(element('starlink-zoom-out').disabled,true);
 layers[0].checked=false;vm.runInContext('rotation=2;setZoom(2)',sandbox);element('starlink-view-reset').events.click();
 assert.equal(vm.runInContext('rotation',sandbox),-.28);assert.equal(element('starlink-globe').dataset.zoom,'1');assert.equal(layers[0].checked,false);
});

test('zoom enlarges the complete drawing surface, bounds memory and leaves ordinary scrolling available',()=>{
 const {sandbox,element}=setup();element('starlink-panel').hidden=false;
 const canvas=element('starlink-globe'),stage=element('starlink-stage');stage.clientWidth=800;stage.clientHeight=760;
 canvas.style.setProperty=(_,value)=>{canvas.clientWidth=canvas.clientHeight=parseFloat(value);};
 sandbox.devicePixelRatio=2;vm.runInContext('setZoom(3)',sandbox);
 assert.equal(canvas.clientWidth,2280);assert.ok(canvas.width<=2560);assert.ok(canvas.height<=2560);
 assert.equal(stage.scrollTop,(2280-760)/2);assert.equal(stage.scrollLeft,(2280-800)/2);
 let prevented=false;canvas.events.wheel({deltaY:1,preventDefault(){prevented=true;}});assert.equal(prevented,false);
 assert.equal(element('starlink-motion').checked,true);
});

test('manual ping plays the modem once; muting, closing and hiding stop it without blocking data',async()=>{
 const {sandbox,element,documentEvents}=setup();const audio=element('starlink-ping-sound');
 await element('starlink-ping-button').events.click();assert.equal(audio.plays,1);assert.equal(audio.volume,.35);
 assert.equal(element('starlink-fetch').dataset.state,'error');
 element('starlink-sound-enabled').checked=false;element('starlink-sound-enabled').events.change();assert.equal(audio.currentTime,0);
 await element('starlink-ping-button').events.click();assert.equal(audio.plays,1);
 const pauses=audio.pauses;element('starlink-close').events.click();assert.ok(audio.pauses>pauses);
 sandbox.document.hidden=true;documentEvents.visibilitychange();assert.ok(audio.pauses>pauses+1);
});

test('refresh failure preserves a moving last-known view and marks its data as older',async()=>{
 const {sandbox,element,workers}=setup();
 const now=new Date().toISOString();
 sandbox.fetch=async()=>({ok:true,json:async()=>({retrievedAt:now,oldestEpoch:now,newestEpoch:now,elements:[],onOrbitCount:1,nonOperationalCount:0,statusCoverageComplete:true})});
 await element('starlink-ping-button').events.click();assert.equal(element('starlink-fetch').dataset.state,'calculating');
 const message={count:1,packed:new Float32Array([7000,0,0,0,0,0]),statuses:new Uint8Array([0]),speed:1,sentAt:Date.now(),timestamp:1};
 workers[0].onmessage({data:message});assert.equal(element('starlink-fetch').dataset.state,'ready');assert.equal(element('starlink-data-quality').dataset.state,'fresh');
 sandbox.fetch=async()=>{throw Error('Offline');};await element('starlink-ping-button').events.click();
 assert.equal(element('starlink-fetch').dataset.state,'error');assert.equal(element('starlink-data-quality').dataset.state,'stale');
 workers[0].onmessage({data:{...message,timestamp:2}});assert.equal(element('starlink-globe').dataset.positionTime,'2');
 assert.equal(element('starlink-fetch').dataset.state,'error');
});
test('layers filter rendered satellites independently while catalog count remains unchanged',()=>{
 const {sandbox,element,layers}=setup();element('starlink-panel').hidden=false;
 vm.runInContext('frame={count:4,packed:new Float32Array([7000,0,0,0,0,0,7000,0,0,0,0,0,7000,0,0,0,0,0,7000,0,0,0,0,0]),statuses:new Uint8Array([0,1,2,3]),speed:1,sentAt:Date.now()};draw()',sandbox);
 assert.equal(element('starlink-globe').dataset.visibleCount,'4');
 for(const layer of layers)layer.checked=false;layers[1].checked=true;layers[1].events.change();assert.equal(element('starlink-globe').dataset.visibleCount,'1');
 layers[1].checked=false;layers[1].events.change();assert.equal(element('starlink-globe').dataset.visibleCount,'0');assert.equal(vm.runInContext('frame.count',sandbox),4);
});

test('layer coverage reports propagated positions and disables unavailable groups',()=>{
 const {sandbox,element,layers}=setup();
 vm.runInContext('updateLayerCoverage({count:3,statuses:new Uint8Array([0,1,1])},new Intl.NumberFormat("en"))',sandbox);
 assert.equal(element('[data-layer-count="0"]').textContent,'1');
 assert.equal(element('[data-layer-count="1"]').textContent,'2');
 assert.equal(element('[data-layer-count="2"]').textContent,'0');
 assert.equal(layers[1].disabled,false);assert.equal(layers[2].disabled,true);
 vm.runInContext('updateLayerCoverage({count:2},new Intl.NumberFormat("en"))',sandbox);
 assert.equal(element('[data-layer-count="0"]').textContent,'2');assert.equal(layers[1].disabled,true);
});
