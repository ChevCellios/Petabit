import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {randomBytes} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {gunzipSync,gzipSync} from 'node:zlib';

const docker=args=>execFileSync('docker',args,{encoding:'utf8',stdio:['ignore','pipe','pipe'],timeout:120000,maxBuffer:1024*1024}).trim();
const compose=['compose','-p','petabit-security','-f',fileURLToPath(new URL('./compose.yml',import.meta.url))];
const container=service=>{
    const id=docker([...compose,'ps','-q',service]);
    assert.match(id,/^[a-f0-9]{12,64}$/,'Expected one running private lab container.');return id;
};
const app=container('petabit-qa-app'),proxy=container('petabit-qa');
const image=docker(['inspect','--format','{{.Image}}',app]);
assert.match(image,/^sha256:[a-f0-9]{64}$/);
assert.equal(docker(['network','inspect','--format','{{.Internal}}','petabit-security-lab']),'true','Runtime tests require the no-egress lab network.');
const probe=code=>docker(['exec',proxy,'node','--input-type=module','-e',code]);
function verifyIdentity(id) {
    const status=docker(['exec',id,'cat','/proc/1/status']);
    for(const field of ['Uid','Gid'])assert.deepEqual(new RegExp(`^${field}:\\s+(.+)$`,'m').exec(status)?.[1].trim().split(/\s+/),['1654','1654','1654','1654']);
    assert.equal(/^Name:\s+(.+)$/m.exec(status)?.[1].trim(),'dotnet');
    assert.equal(BigInt('0x'+/^CapEff:\s+([a-f\d]+)$/m.exec(status)?.[1]),0n);
    assert.equal(/^NoNewPrivs:\s+(\d)$/m.exec(status)?.[1],'1');
}
verifyIdentity(app);
probe(`import assert from 'node:assert/strict';
for(const [culture,text] of [['hr','Minimalni prikaz'],['en','Minimal view'],['de','Minimalansicht']]) {
 const r=await fetch('http://127.0.0.1:3000/Home/ISSTracker',{headers:{'accept-language':culture},signal:AbortSignal.timeout(10000)});
 assert.equal(r.status,200);const html=await r.text();assert.ok(html.includes('<html lang="'+culture+'">'));assert.ok(html.includes(text));
}`);

const nonce=randomBytes(8).toString('hex'),volume='petabit-runtime-'+nonce,created=[];
const fixture=JSON.parse(gunzipSync(fs.readFileSync(new URL('../../Petabit/Data/starlink-bootstrap.json.gz',import.meta.url))));
assert.ok(fixture.Elements.length>1000,'Exercise the real orbital dataset and native gzip decompression.');
fixture.RetrievedAt=new Date(Date.now()-60000).toISOString();
const temporary=fs.mkdtempSync(path.join(os.tmpdir(),'petabit-runtime-regression-'));
const bootstrap=path.join(temporary,'bootstrap.json.gz'),emptyBootstrap=path.join(temporary,'no-bootstrap.json.gz');
let ownsVolume=false,testError;
const cleanupErrors=[];
function create(args) {
    const id=docker(['create',...args]);assert.match(id,/^[a-f0-9]{64}$/);created.push(id);return id;
}
function startApplication(suffix,user,bootstrapFile) {
    const name='petabit-runtime-'+nonce+'-'+suffix;
    const args=['--name',name,'--network','petabit-security-lab','--network-alias',name,'--user',user,
        '--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true',
        '--tmpfs','/tmp:uid=1654,gid=1654,mode=700',
        '--mount',`type=volume,source=${volume},target=/app/App_Data`,
        '--mount',`type=bind,source=${bootstrapFile},target=/app/Data/starlink-bootstrap.json.gz,readonly`,
        '-e','PORT=3417','-e','ASPNETCORE_ENVIRONMENT=Production','-e',`AllowedHosts=${name}`,'-e','StationSync__Enabled=false'];
    if(user==='0')for(const capability of ['CHOWN','DAC_OVERRIDE','SETUID','SETGID'])args.push('--cap-add',capability);
    const id=create([...args,image]);docker(['start',id]);
    probe(`let ready=false;for(let n=0;n<40;n++){try{const r=await fetch('http://${name}:3417/health/live',{signal:AbortSignal.timeout(1000)});if(r.status===200){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,250));}if(!ready)throw Error('Dynamic-port runtime unavailable.');`);
    verifyIdentity(id);
    probe(`import assert from 'node:assert/strict';const r=await fetch('http://${name}:3417/Home/StarlinkData',{signal:AbortSignal.timeout(110000)});assert.equal(r.status,200);const d=await r.json();assert.equal(d.elements.length,${fixture.Elements.length});assert.equal(Date.parse(d.retrievedAt),${Date.parse(fixture.RetrievedAt)});assert.equal(d.persistenceFailed,false);`);
    return id;
}
try {
    fs.writeFileSync(bootstrap,gzipSync(Buffer.from(JSON.stringify(fixture))),{mode:0o644});
    // The second process must recover persisted data, without a usable bundled fallback.
    fs.writeFileSync(emptyBootstrap,gzipSync(Buffer.from('null')),{mode:0o644});
    assert.equal(docker(['volume','create','--label',`petabit.runtime-regression=${nonce}`,volume]),volume);
    ownsVolume=docker(['volume','inspect','--format','{{index .Labels "petabit.runtime-regression"}}',volume])===nonce;
    assert.ok(ownsVolume,'Only a newly owned fixture volume may be used.');
    const seed=create(['--network','none','--user','0','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true',
        '--mount',`type=volume,source=${volume},target=/seed`,'--entrypoint','/bin/sh',image,'-c',
        'chmod 700 /seed; printf "root-owned fixture" > /seed/ownership-check.txt; chmod 600 /seed/ownership-check.txt; ln -s /app/Petabit.dll /seed/outside-link']);
    docker(['start','--attach',seed]);
    const initialized=startApplication('root','0',bootstrap);
    assert.equal(docker(['exec',initialized,'stat','-c','%u:%g:%a','/app/App_Data/ownership-check.txt']),'1654:1654:600');
    assert.equal(docker(['exec',initialized,'stat','-c','%u:%g','/app/App_Data/starlink-status.json']),'1654:1654');
    assert.equal(docker(['exec',initialized,'stat','-c','%u:%g','/app/App_Data/outside-link']),'1654:1654');
    assert.equal(docker(['exec',initialized,'stat','-c','%u:%g','/app/Petabit.dll']),'0:0','Volume initialization must not follow the out-of-volume symbolic link.');
    docker(['stop','--time','10',initialized]);
    startApplication('restored','1654:1654',emptyBootstrap);
} catch(error) {testError=error;}
finally {
    for(const id of [...created].reverse())try{docker(['rm','--force',id]);}catch(error){cleanupErrors.push(error);}
    if(ownsVolume)try{docker(['volume','rm',volume]);}catch(error){cleanupErrors.push(error);}
    const resolved=path.resolve(temporary),parent=path.resolve(os.tmpdir());
    if(path.dirname(resolved)!==parent||!path.basename(resolved).startsWith('petabit-runtime-regression-'))cleanupErrors.push(new Error('Unexpected temporary fixture path.'));
    else try{fs.rmSync(resolved,{recursive:true,force:true});}catch(error){cleanupErrors.push(error);}
}
if(testError||cleanupErrors.length)throw new AggregateError([testError,...cleanupErrors].filter(Boolean),'Private runtime regression failed.');
console.log(JSON.stringify({passed:true,checks:['non-root PID 1','zero effective capabilities','no-new-privileges','hr/en/de resources','root-owned volume initialization','symbolic-link ownership without following target','dynamic PORT','native gzip and large Starlink bootstrap','persisted data on second container'],elements:fixture.Elements.length}));
