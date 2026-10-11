import path from 'node:path';
import fs from 'node:fs';
import {execFileSync} from 'node:child_process';
const restored=path.resolve(process.argv[2]), source=path.join(restored,'source');
if(!fs.existsSync(path.join(restored,'backup-manifest.json')))throw new Error('Restore manifest required.');
// Restored snapshots are private (0600). Match their runner owner inside the
// container instead of making production data readable by other users.
const uid=process.getuid?.(), gid=process.getgid?.();
if(!Number.isInteger(uid)||uid===0||!Number.isInteger(gid)||gid===0)throw new Error('Recovery drill requires a non-root Linux operator.');
const compose=['compose','-p','petabit-security','-f',path.join(source,'security/qa/compose.yml')];
const run=args=>execFileSync('docker',args,{stdio:['ignore','pipe','pipe'],timeout:600000,maxBuffer:16*1024*1024});
let appStarted=false;
try {
    run(['build','--file',path.join(source,'Petabit/Dockerfile'),'--tag','petabit:recovery-drill',source]);
    run([...compose,'up','-d','--no-deps','petabit-qa']);
    run(['run','-d','--name','petabit-recovery-drill','--network','petabit-security-lab','--network-alias','petabit-qa-app','--user',`${uid}:${gid}`,'--read-only','--cap-drop','ALL','--security-opt','no-new-privileges:true','--tmpfs',`/tmp:uid=${uid},gid=${gid},mode=700`,'--mount',`type=bind,source=${path.join(restored,'App_Data')},target=/app/App_Data,readonly`,'-e','PORT=3000','-e','ASPNETCORE_ENVIRONMENT=Production','-e','AllowedHosts=petabit-qa','-e','StationSync__Enabled=true','-e','ForwardedHeaders__KnownProxies__0=172.30.251.3','petabit:recovery-drill']);appStarted=true;
    run([...compose,'exec','-T','petabit-qa','node','/lab/wait-ready.mjs']);
    run([...compose,'exec','-T','petabit-qa','node','/lab/probe.mjs']);
    for(const route of ['StationData','StarlinkData'])run([...compose,'exec','-T','petabit-qa','node','--input-type=module','-e',`const r=await fetch('http://localhost:3000/Home/${route}',{signal:AbortSignal.timeout(30000)});const d=await r.json();if(r.status!==200||!${route==='StationData'?'d.stationStatusCheckedAt':'d.elements?.length'})throw Error('Restored snapshot unavailable');`]);
} finally {
    try {if(appStarted)run(['rm','--force','petabit-recovery-drill']);}
    finally {run([...compose,'down']);}
}
