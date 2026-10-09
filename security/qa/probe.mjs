import assert from 'node:assert/strict';
const target=process.argv[2]||'http://127.0.0.1:3000';
if(!/^http:\/\/(127\.0\.0\.1|localhost|petabit-qa):\d+$/.test(target))throw new Error('Security probes are restricted to the private lab.');
const get=(route,options={})=>fetch(target+route,{redirect:'manual',signal:AbortSignal.timeout(10000),...options});
const home=await get('/');assert.equal(home.status,200);
const csp=home.headers.get('content-security-policy');assert.ok(csp?.includes("frame-ancestors 'none'"));assert.ok(csp.includes("object-src 'none'"));assert.ok(!csp.includes("'unsafe-inline'"));
assert.equal(home.headers.get('x-content-type-options'),'nosniff');assert.equal(home.headers.get('x-frame-options'),'DENY');assert.equal(home.headers.get('server'),null);
const html=await home.text();assert.ok(html.includes('Petabit'));
for(const route of ['/.git/config','/appsettings.json','/App_Data/starlink-status.json','/Data/starlink-bootstrap.json.gz','/security/backup/archive.mjs']) {
 const response=await get(route);assert.equal(response.status,404,route);
}
const injection='<script>alert("probe")</script>';
const reflected=await get('/?q='+encodeURIComponent(injection));assert.equal(reflected.status,200);assert.ok(!(await reflected.text()).includes(injection));
const post=await get('/Home/SetLanguage',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:'culture=hr&returnUrl=https%3A%2F%2Fattacker.example'});assert.equal(post.status,400);
const huge=await get('/Home/SetLanguage',{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:'x='.padEnd(20000,'x')});assert.ok([400,413].includes(huge.status));
const health=await get('/health/live');assert.equal(health.status,200);
console.log(JSON.stringify({target,passed:true,checks:['security headers','private files unavailable','reflected-script probe','unauthenticated form rejection','oversized request rejection','liveness']}));
