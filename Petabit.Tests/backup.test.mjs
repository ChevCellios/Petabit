import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { entry, seal, open, restore, validate } from '../security/backup/archive.mjs';
const payload=()=>({version:1,files:[entry('App_Data/state.json',Buffer.from('{"ok":true}'))]});
test('encrypted archive round trip preserves exact data and detects wrong key or tampering',()=>{
 const key=randomBytes(32), encrypted=seal(payload(),key);
 assert.equal(open(encrypted,key).files[0].data,payload().files[0].data);
 assert.throws(()=>open(encrypted,randomBytes(32)));
 const tampered=Buffer.from(encrypted);tampered[tampered.length-1]^=1;assert.throws(()=>open(tampered,key));
});
test('unsafe Windows/Unix paths, duplicate paths and corrupt hashes are rejected before restore',()=>{
 for(const name of ['../escape','/absolute','App_Data/../../escape','App_Data\\escape','C:/escape','App_Data/NUL','App_Data/name.']) {
  assert.throws(()=>validate({version:1,files:[entry(name,Buffer.from('x'))]}));
 }
 assert.throws(()=>validate({version:1,files:[entry('a',Buffer.from('x')),entry('A',Buffer.from('y'))]}));
 const bad=payload();bad.files[0].sha256='0'.repeat(64);assert.throws(()=>validate(bad));
});
test('restore creates a new isolated directory and refuses existing destinations',()=>{
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'petabit-restore-test-'));
 try { const destination=path.join(root,'restored');restore(payload(),destination);assert.equal(fs.readFileSync(path.join(destination,'App_Data/state.json'),'utf8'),'{"ok":true}');assert.throws(()=>restore(payload(),destination)); }
 finally {fs.rmSync(root,{recursive:true,force:true});}
});
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const cli=fileURLToPath(new URL('../security/backup/backup.mjs',import.meta.url));
test('CLI backup restores a complete independent Git repository and data without original checkout',()=>{
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'petabit-recovery-drill-'));
 const git=(directory,...args)=>execFileSync('git',['-C',directory,...args],{stdio:['ignore','pipe','pipe']}).toString().trim();
 const run=(...args)=>execFileSync(process.execPath,[cli,...args],{stdio:['ignore','pipe','pipe']});
 try {
  const repo=path.join(root,'repo'),data=path.join(root,'data'),key=path.join(root,'recovery.key'),archive=path.join(root,'backup.pbit'),output=path.join(root,'restored');
  fs.mkdirSync(repo);fs.mkdirSync(data);git(repo,'init','--initial-branch=main');git(repo,'config','user.email','backup-test@example.invalid');git(repo,'config','user.name','Backup test');
  fs.writeFileSync(path.join(repo,'app.txt'),'verified source');git(repo,'add','.');git(repo,'commit','-m','Recovery fixture');git(repo,'tag','verified-release');
  const revision=git(repo,'rev-parse','HEAD');fs.writeFileSync(path.join(data,'status.json'),'{"checked":true}');
  run('keygen','--key',key);run('create','--repository',repo,'--data',data,'--data-source','test-fixture','--key',key,'--output',archive);
  assert.throws(()=>run('create','--repository',repo,'--data',data,'--key',key,'--output',archive));
  run('restore','--archive',archive,'--key',key,'--destination',output);
  const clone=path.join(output,'source');execFileSync('git',['clone',path.join(output,'repository.bundle'),clone],{stdio:'pipe'});
  assert.equal(git(clone,'rev-parse','HEAD'),revision);assert.equal(git(clone,'rev-parse','verified-release'),revision);git(clone,'fsck','--full');
  assert.equal(fs.readFileSync(path.join(clone,'app.txt'),'utf8'),'verified source');assert.equal(fs.readFileSync(path.join(output,'App_Data/status.json'),'utf8'),'{"checked":true}');
  fs.writeFileSync(path.join(repo,'app.txt'),'uncommitted');assert.throws(()=>run('create','--repository',repo,'--data',data,'--key',key,'--output',path.join(root,'dirty.pbit')));
 } finally {fs.rmSync(root,{recursive:true,force:true});}
});
