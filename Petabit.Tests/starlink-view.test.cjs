const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
function setup(){
 const elements=new Map();
 const element=id=>{if(!elements.has(id)) elements.set(id,{dataset:{},checked:true,value:'1',hidden:true,clientWidth:800,clientHeight:500,events:{},addEventListener(type,fn){this.events[type]=fn;},setAttribute(){},focus(){}});return elements.get(id);};
 const layers=Array.from({length:4},(_,i)=>element('layer'+i));
 const context2d=new Proxy({createRadialGradient(){return {addColorStop(){}};}},{get:(obj,key)=>key in obj?obj[key]:()=>{}});
 element('starlink-globe').getContext=()=>context2d;
 element('starlink-panel').querySelectorAll=()=>layers;
 const sandbox=vm.createContext({document:{getElementById:element,hidden:false,body:{classList:{contains:()=>false}},addEventListener(){}},window:{matchMedia:()=>({matches:true}),addEventListener(){}},devicePixelRatio:1,cancelAnimationFrame(){},requestAnimationFrame(){return 1;},console,Date,Math,Number,String,Array});
 vm.runInContext(fs.readFileSync('Petabit/wwwroot/js/starlink-dashboard.js','utf8'),sandbox);
 return {sandbox,element,layers};
}
test('zoom clamps to usable limits and reset restores orientation without changing layers',()=>{
 const {sandbox,element,layers}=setup();
 vm.runInContext('setZoom(99)',sandbox);assert.equal(element('starlink-globe').dataset.zoom,'3');assert.equal(element('starlink-zoom-in').disabled,true);
 vm.runInContext('setZoom(-5)',sandbox);assert.equal(element('starlink-globe').dataset.zoom,'1');assert.equal(element('starlink-zoom-out').disabled,true);
 layers[0].checked=false;vm.runInContext('rotation=2;setZoom(2)',sandbox);element('starlink-view-reset').events.click();
 assert.equal(vm.runInContext('rotation',sandbox),-.28);assert.equal(element('starlink-globe').dataset.zoom,'1');assert.equal(layers[0].checked,false);
});
test('layers filter rendered satellites independently while catalog count remains unchanged',()=>{
 const {sandbox,element,layers}=setup();element('starlink-panel').hidden=false;
 vm.runInContext('frame={count:4,packed:new Float32Array([7000,0,0,0,0,0,7000,0,0,0,0,0,7000,0,0,0,0,0,7000,0,0,0,0,0]),statuses:new Uint8Array([0,1,2,3]),speed:1,sentAt:Date.now()};draw()',sandbox);
 assert.equal(element('starlink-globe').dataset.visibleCount,'4');
 for(const layer of layers)layer.checked=false;layers[1].checked=true;layers[1].events.change();assert.equal(element('starlink-globe').dataset.visibleCount,'1');
 layers[1].checked=false;layers[1].events.change();assert.equal(element('starlink-globe').dataset.visibleCount,'0');assert.equal(vm.runInContext('frame.count',sandbox),4);
});
