import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {execFileSync,spawnSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {restoreCheckpoint,selectActiveDeployment,assertStableDeployment} from '../security/backup/cloud.mjs';
import {open} from '../security/backup/archive.mjs';

const available=process.platform==='linux'&&spawnSync('rclone',['version']).status===0;
const deploymentId='11111111-1111-4111-8111-111111111111';
const active=()=>({id:deploymentId,status:'SUCCESS',meta:{commitHash:'a'.repeat(40),buildOnly:false}});
test('active revision comes from the unique successful deployment while newer builds wait',()=>{
 for(const status of ['WAITING','BUILDING']) {
  const pending={id:'22222222-2222-4222-8222-222222222222',status,meta:{commitHash:'b'.repeat(40)}};
  assert.deepEqual(selectActiveDeployment([pending,active()]),{id:deploymentId,revision:'a'.repeat(40)});
 }
});
test('missing, ambiguous, invalid and transitioning deployment metadata is rejected',()=>{
 const badId=active();badId.id=['11111111-1111-4111-8111-111111111111'];
 const badSha=active();badSha.meta.commitHash=['a'.repeat(40)];
 const missingSha=active();delete missingSha.meta.commitHash;
 const buildOnly=active();buildOnly.meta.buildOnly=true;
 for(const deployments of [null,{},[],[null],[{}],[active(),null],[badId],[badSha],[missingSha],[buildOnly],
  [active(),active()],[active(),{...active(),id:'22222222-2222-4222-8222-222222222222'}],
  ...['DEPLOYING','REMOVING'].map(status=>[active(),{id:'22222222-2222-4222-8222-222222222222',status}])]) {
  assert.throws(()=>selectActiveDeployment(deployments));
 }
 assert.throws(()=>selectActiveDeployment([active(),{id:123,status:'REMOVED'}]));
 assert.throws(()=>selectActiveDeployment([active(),{id:active().id,status:'REMOVED'}]));
});
test('stable deployment comparison allows old terminal records and builds that remain pending',()=>{
 const pending={id:'22222222-2222-4222-8222-222222222222',status:'WAITING'};
 const historical={id:'33333333-3333-4333-8333-333333333333',status:'REMOVED'};
 const initial=[active(),pending,historical];
 for(const status of ['WAITING','BUILDING']) {
  const additional={id:'44444444-4444-4444-8444-444444444444',status};
  assert.doesNotThrow(()=>assertStableDeployment(initial,[active(),{...pending,status},historical,additional]));
 }
});
test('a rollout that starts and fails between export checks cannot masquerade as stable old production',()=>{
 const pending={id:'22222222-2222-4222-8222-222222222222',status:'WAITING'};
 for(const status of ['FAILED','CRASHED','REMOVED','CANCELED']) {
  assert.throws(()=>assertStableDeployment([active(),pending],[active(),{...pending,status}]));
  assert.throws(()=>assertStableDeployment([active()],[active(),{...pending,status}]));
 }
});
function fixture({failDownload=false,deploymentBefore='stable',deploymentAfter='stable'}={}) {
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'petabit-cloud-test-'));
 const repo=path.join(root,'repo'),bin=path.join(root,'bin'),temp=path.join(root,'temp'),remote=path.join(root,'remote');
 for(const directory of [repo,bin,temp,remote])fs.mkdirSync(directory);
 const git=(...args)=>execFileSync('git',['-C',repo,...args],{stdio:'pipe'});
 git('init','--initial-branch=master');git('config','user.name','Backup test');git('config','user.email','backup@example.invalid');
 fs.writeFileSync(path.join(repo,'source.txt'),'recoverable source');git('add','.');git('commit','-m','Fixture');
 const deployed=git('rev-parse','HEAD').toString().trim();
 fs.writeFileSync(path.join(repo,'waiting.txt'),'source not yet deployed');git('add','.');git('commit','-m','Waiting deployment');
 const head=git('rev-parse','HEAD').toString().trim();
 const executable=(name,code)=>fs.writeFileSync(path.join(bin,name),'#!/usr/bin/env node\n'+code,{mode:0o755});
 executable('gh',`if(process.argv[2]==='run'&&process.argv[3]==='list')console.log('[]');else process.exit(1);`);
 executable('railway',`
const fs=require('node:fs'),path=require('node:path'),a=process.argv.slice(2);
fs.appendFileSync(process.env.FIXTURE_EVENTS,JSON.stringify(a)+'\\n');
if(a[0]==='link'||process.env.RAILWAY_PROJECT_ID!=='20c1e1d1-4daf-4012-b3ab-29cdf8abdca9'||process.env.RAILWAY_ENVIRONMENT_ID!=='f5334489-9c09-4614-b3aa-1d33c6543f02'||process.env.RAILWAY_SERVICE_ID!=='9957dae9-1a2b-47e3-9316-b180923b05f1')process.exit(1);
if(a[0]==='deployment'&&a[1]==='list') {
 const reads=fs.readFileSync(process.env.FIXTURE_EVENTS,'utf8').trim().split('\\n').map(JSON.parse).filter(x=>x[0]==='deployment').length;
 const phase=reads===1?process.env.FIXTURE_DEPLOYMENT_BEFORE:process.env.FIXTURE_DEPLOYMENT_AFTER;
 const current={id:'${deploymentId}',status:'SUCCESS',meta:{commitHash:process.env.FIXTURE_DEPLOYED_COMMIT,buildOnly:false}};
 const pending={id:'22222222-2222-4222-8222-222222222222',status:'WAITING',meta:{commitHash:process.env.FIXTURE_HEAD_COMMIT}};
 if(phase==='changed-id')current.id='33333333-3333-4333-8333-333333333333';
 if(phase==='changed-sha')current.meta.commitHash=process.env.FIXTURE_HEAD_COMMIT;
 if(phase==='deploying')pending.status='DEPLOYING';
 if(phase==='removing')pending.status='REMOVING';
 if(phase==='failed-roundtrip')pending.status='FAILED';
 if(phase==='crashed-roundtrip')pending.status='CRASHED';
 console.log(JSON.stringify(phase==='missing'?[pending]:[pending,current]));
}
if(a.includes('download')) {
 if(process.env.FIXTURE_FAIL==='true')process.exit(1);
 const dir=a[a.indexOf('download')+2];fs.mkdirSync(dir);
 fs.writeFileSync(path.join(dir,'station-status.json'),'{}');fs.writeFileSync(path.join(dir,'starlink-status.json'),'{}');
}`);
 const key=randomBytes(32), events=path.join(root,'events.jsonl');
 const env={...process.env,PATH:bin+path.delimiter+process.env.PATH,RAILWAY_API_TOKEN:'test-only',PETABIT_BACKUP_KEY_BASE64:key.toString('base64'),PETABIT_ONEDRIVE_CONFIG:`[petabit]\ntype = alias\nremote = ${remote}\n`,GITHUB_REPOSITORY:'fixture/repo',GITHUB_RUN_ID:'12345',GITHUB_REF:'refs/heads/master',GITHUB_EVENT_NAME:'workflow_dispatch',RUNNER_TEMP:temp,GH_TOKEN:'test-only',FIXTURE_EVENTS:events,FIXTURE_FAIL:String(failDownload),FIXTURE_DEPLOYMENT_BEFORE:deploymentBefore,FIXTURE_DEPLOYMENT_AFTER:deploymentAfter,FIXTURE_DEPLOYED_COMMIT:deployed,FIXTURE_HEAD_COMMIT:head,PETABIT_RESTORE_DRILL:'false'};
 const moduleUrl=new URL('../security/backup/cloud.mjs',import.meta.url).href;
 const run=()=>execFileSync(process.execPath,['--input-type=module','-e',`import {runCloudBackup} from ${JSON.stringify(moduleUrl)};await runCloudBackup({now:new Date('2026-10-02T10:00:00Z'),remoteType:'alias'});`],{cwd:repo,env,stdio:'pipe',timeout:120000});
 return {root,remote,temp,key,events,run,deployed,head};
}
test('cloud pipeline uploads, downloads and restores through real rclone without production credentials',{skip:!available},()=>{
 const f=fixture();
 try {
  f.run();const receipt=JSON.parse(fs.readFileSync(path.join(f.temp,'backup-receipt.json')));
  assert.equal(receipt.remoteReadbackVerified,true);assert.equal(receipt.gitRestoreVerified,true);assert.equal(receipt.temporarySshKeyRevoked,true);
  assert.notEqual(f.deployed,f.head);assert.equal(receipt.revision,f.deployed);assert.equal(receipt.deploymentId,deploymentId);
  const uploaded=path.join(f.remote,'Petabit Backups/Automated/petabit-2026-10-02-12345-production.pbit');assert.ok(fs.existsSync(uploaded));
  assert.equal(open(fs.readFileSync(uploaded),f.key).revision,f.deployed);
  assert.ok(restoreCheckpoint(fs.readFileSync(path.join(f.temp,'onedrive-state.pbit')),f.key).includes(Buffer.from('[petabit]')));
  const commands=fs.readFileSync(f.events,'utf8').trim().split('\n').map(line=>JSON.parse(line));
  assert.equal(commands.filter(a=>a.includes('add')).length,1);assert.equal(commands.filter(a=>a.includes('remove')).length,1);
  assert.equal(commands.filter(a=>a[0]==='deployment'&&a[1]==='list').length,2);
  assert.ok(commands.findIndex(a=>a.includes('remove'))<commands.findLastIndex(a=>a[0]==='deployment'));
  const work=fs.readdirSync(f.temp).find(name=>name.startsWith('petabit-backup-'));
  for(const file of ['rclone.conf','backup.recovery-key','home/.ssh/id_ed25519','home/.ssh/id_ed25519.pub'])assert.equal(fs.existsSync(path.join(f.temp,work,file)),false);
 } finally {fs.rmSync(f.root,{recursive:true,force:true});}
});
test('cloud pipeline revokes temporary SSH access when production export fails',{skip:!available},()=>{
 const f=fixture({failDownload:true});
 try {
  assert.throws(()=>f.run());
  const commands=fs.readFileSync(f.events,'utf8').trim().split('\n').map(line=>JSON.parse(line));
  assert.equal(commands.filter(a=>a.includes('remove')).length,1);
  assert.equal(fs.existsSync(path.join(f.temp,'backup-receipt.json')),false);
  assert.equal(fs.existsSync(path.join(f.remote,'Petabit Backups')),false);
 } finally {fs.rmSync(f.root,{recursive:true,force:true});}
});

for(const deploymentAfter of ['changed-id','changed-sha','missing','deploying','removing','failed-roundtrip','crashed-roundtrip']) {
 test(`cloud pipeline refuses ${deploymentAfter} after export and revokes SSH without uploading`,{skip:!available},()=>{
  const f=fixture({deploymentAfter});
  try {
   assert.throws(()=>f.run());
   const commands=fs.readFileSync(f.events,'utf8').trim().split('\n').map(line=>JSON.parse(line));
   assert.equal(commands.filter(a=>a.includes('add')).length,1);assert.equal(commands.filter(a=>a.includes('remove')).length,1);
   assert.equal(commands.filter(a=>a.includes('download')).length,1);
   assert.ok(commands.findIndex(a=>a.includes('remove'))<commands.findLastIndex(a=>a[0]==='deployment'));
   assert.equal(fs.existsSync(path.join(f.temp,'backup-receipt.json')),false);
   assert.equal(fs.existsSync(path.join(f.remote,'Petabit Backups')),false);
  } finally {fs.rmSync(f.root,{recursive:true,force:true});}
 });
}
for(const deploymentBefore of ['missing','deploying']) {
 test(`cloud pipeline refuses ${deploymentBefore} before registering temporary access`,{skip:!available},()=>{
  const f=fixture({deploymentBefore});
  try {
   assert.throws(()=>f.run());
   const commands=fs.readFileSync(f.events,'utf8').trim().split('\n').map(line=>JSON.parse(line));
   assert.equal(commands.filter(a=>a.includes('add')||a.includes('download')||a.includes('remove')).length,0);
   assert.equal(fs.existsSync(path.join(f.temp,'backup-receipt.json')),false);
   assert.equal(fs.existsSync(path.join(f.remote,'Petabit Backups')),false);
  } finally {fs.rmSync(f.root,{recursive:true,force:true});}
 });
}

test('application drill reads private snapshots as their non-root owner without relaxing file permissions',{skip:process.platform!=='linux'||process.getuid?.()===0||process.getgid?.()===0},()=>{
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'petabit-drill-test-'));
 try {
  const bin=path.join(root,'bin'),restored=path.join(root,'restored'),events=path.join(root,'events.jsonl');
  fs.mkdirSync(bin);fs.mkdirSync(restored,{mode:0o700});fs.mkdirSync(path.join(restored,'App_Data'),{mode:0o700});
  fs.writeFileSync(path.join(restored,'backup-manifest.json'),'{}',{mode:0o600});
  const snapshot=path.join(restored,'App_Data','station-status.json');fs.writeFileSync(snapshot,'{}',{mode:0o600});
  fs.writeFileSync(path.join(bin,'docker'),`#!/usr/bin/env node\nrequire('node:fs').appendFileSync(process.env.FIXTURE_EVENTS,JSON.stringify(process.argv.slice(2))+'\\n');`,{mode:0o755});
  execFileSync(process.execPath,[new URL('../security/backup/drill.mjs',import.meta.url).pathname,restored],{env:{...process.env,PATH:bin+path.delimiter+process.env.PATH,FIXTURE_EVENTS:events},stdio:'pipe'});
  const commands=fs.readFileSync(events,'utf8').trim().split('\n').map(line=>JSON.parse(line));
  const app=commands.find(a=>a[0]==='run');
  assert.equal(app[app.indexOf('--user')+1],`${process.getuid()}:${process.getgid()}`);
  assert.ok(app.includes('--read-only'));assert.ok(app.includes('no-new-privileges:true'));
  assert.ok(app[app.indexOf('--mount')+1].endsWith(',readonly'));
  assert.equal(fs.statSync(snapshot).mode&0o777,0o600);
  assert.ok(commands.some(a=>a[0]==='rm'&&a.includes('petabit-recovery-drill')));
 } finally {fs.rmSync(root,{recursive:true,force:true});}
});
