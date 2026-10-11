import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {execFileSync,spawnSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {restoreCheckpoint} from '../security/backup/cloud.mjs';

const available=process.platform==='linux'&&spawnSync('rclone',['version']).status===0;
function fixture(failDownload) {
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'petabit-cloud-test-'));
 const repo=path.join(root,'repo'),bin=path.join(root,'bin'),temp=path.join(root,'temp'),remote=path.join(root,'remote');
 for(const directory of [repo,bin,temp,remote])fs.mkdirSync(directory);
 const git=(...args)=>execFileSync('git',['-C',repo,...args],{stdio:'pipe'});
 git('init','--initial-branch=master');git('config','user.name','Backup test');git('config','user.email','backup@example.invalid');
 fs.writeFileSync(path.join(repo,'source.txt'),'recoverable source');git('add','.');git('commit','-m','Fixture');
 const executable=(name,code)=>fs.writeFileSync(path.join(bin,name),'#!/usr/bin/env node\n'+code,{mode:0o755});
 executable('gh',`if(process.argv[2]==='run'&&process.argv[3]==='list')console.log('[]');else process.exit(1);`);
 executable('railway',`const fs=require('node:fs'),path=require('node:path');const a=process.argv.slice(2);fs.appendFileSync(process.env.FIXTURE_EVENTS,JSON.stringify(a)+'\\n');if(a.includes('download')){if(process.env.FIXTURE_FAIL==='true')process.exit(1);const dir=a[a.indexOf('download')+2];fs.mkdirSync(dir);fs.writeFileSync(path.join(dir,'station-status.json'),'{}');fs.writeFileSync(path.join(dir,'starlink-status.json'),'{}');}`);
 const key=randomBytes(32), events=path.join(root,'events.jsonl');
 const env={...process.env,PATH:bin+path.delimiter+process.env.PATH,RAILWAY_API_TOKEN:'test-only',PETABIT_BACKUP_KEY_BASE64:key.toString('base64'),PETABIT_ONEDRIVE_CONFIG:`[petabit]\ntype = alias\nremote = ${remote}\n`,GITHUB_REPOSITORY:'fixture/repo',GITHUB_RUN_ID:'12345',GITHUB_REF:'refs/heads/master',GITHUB_EVENT_NAME:'workflow_dispatch',RUNNER_TEMP:temp,GH_TOKEN:'test-only',FIXTURE_EVENTS:events,FIXTURE_FAIL:String(failDownload),PETABIT_RESTORE_DRILL:'false'};
 const moduleUrl=new URL('../security/backup/cloud.mjs',import.meta.url).href;
 const run=()=>execFileSync(process.execPath,['--input-type=module','-e',`import {runCloudBackup} from ${JSON.stringify(moduleUrl)};await runCloudBackup({now:new Date('2026-10-02T10:00:00Z')});`],{cwd:repo,env,stdio:'pipe',timeout:120000});
 return {root,remote,temp,key,events,run};
}
test('cloud pipeline uploads, downloads and restores through real rclone without production credentials',{skip:!available},()=>{
 const f=fixture(false);
 try {
  f.run();const receipt=JSON.parse(fs.readFileSync(path.join(f.temp,'backup-receipt.json')));
  assert.equal(receipt.remoteReadbackVerified,true);assert.equal(receipt.gitRestoreVerified,true);assert.equal(receipt.temporarySshKeyRevoked,true);
  const uploaded=path.join(f.remote,'Petabit Backups/Automated/petabit-2026-10-02-12345-production.pbit');assert.ok(fs.existsSync(uploaded));
  assert.ok(restoreCheckpoint(fs.readFileSync(path.join(f.temp,'onedrive-state.pbit')),f.key).includes(Buffer.from('[petabit]')));
  const commands=fs.readFileSync(f.events,'utf8').trim().split('\n').map(line=>JSON.parse(line));
  assert.equal(commands.filter(a=>a.includes('add')).length,1);assert.equal(commands.filter(a=>a.includes('remove')).length,1);
  const work=fs.readdirSync(f.temp).find(name=>name.startsWith('petabit-backup-'));
  for(const file of ['rclone.conf','backup.recovery-key','home/.ssh/id_ed25519','home/.ssh/id_ed25519.pub'])assert.equal(fs.existsSync(path.join(f.temp,work,file)),false);
 } finally {fs.rmSync(f.root,{recursive:true,force:true});}
});
test('cloud pipeline revokes temporary SSH access when production export fails',{skip:!available},()=>{
 const f=fixture(true);
 try {
  assert.throws(()=>f.run());
  const commands=fs.readFileSync(f.events,'utf8').trim().split('\n').map(line=>JSON.parse(line));
  assert.equal(commands.filter(a=>a.includes('remove')).length,1);
  assert.equal(fs.existsSync(path.join(f.temp,'backup-receipt.json')),false);
  assert.equal(fs.existsSync(path.join(f.remote,'Petabit Backups')),false);
 } finally {fs.rmSync(f.root,{recursive:true,force:true});}
});
