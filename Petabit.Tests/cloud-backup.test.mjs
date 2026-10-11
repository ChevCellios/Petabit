import {test} from 'node:test';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
import {entry,seal} from '../security/backup/archive.mjs';
import {verifyRemoteCopy,checkpoint,restoreCheckpoint} from '../security/backup/cloud.mjs';

const production=()=>({version:1,dataSource:'railway-volume-export',revision:'a'.repeat(40),files:[entry('repository.bundle',Buffer.from('fixture')),entry('App_Data/station-status.json',Buffer.from('{"Crew":[]}')),entry('App_Data/starlink-status.json',Buffer.from('{"Elements":[]}'))]});
test('remote backup requires byte-identical authenticated production data, not a local preview',()=>{
 const key=randomBytes(32),payload=production(),archive=seal(payload,key);
 assert.equal(verifyRemoteCopy(archive,archive,key).revision,payload.revision);
 const tampered=Buffer.from(archive);tampered[tampered.length-1]^=1;
 assert.throws(()=>verifyRemoteCopy(archive,tampered,key));
 assert.throws(()=>verifyRemoteCopy(tampered,tampered,key));
 assert.throws(()=>verifyRemoteCopy(archive,archive,randomBytes(32)));
 payload.dataSource='local-preview';const local=seal(payload,key);
 assert.throws(()=>verifyRemoteCopy(local,local,key));
});
test('remote backup rejects missing snapshots, invalid JSON and invalid source revision',()=>{
 const key=randomBytes(32);
 for(const mutation of [p=>p.files.pop(),p=>p.files[1]=entry('App_Data/station-status.json',Buffer.from('broken')),p=>p.revision='../../untrusted']) {
  const payload=production();mutation(payload);const archive=seal(payload,key);assert.throws(()=>verifyRemoteCopy(archive,archive,key));
 }
});
test('OAuth refresh checkpoints are encrypted and cannot be confused with a recovery archive',()=>{
 const key=randomBytes(32),config=Buffer.from('[petabit]\ntoken = secret-test-token');
 const bytes=checkpoint(config,key);assert.ok(!bytes.includes(Buffer.from('secret-test-token')));
 assert.deepEqual(restoreCheckpoint(bytes,key),config);
 assert.throws(()=>restoreCheckpoint(bytes,randomBytes(32)));
 assert.throws(()=>restoreCheckpoint(seal(production(),key),key));
 const bad=seal({version:1,dataSource:'onedrive-credential-checkpoint',files:[entry('other.conf',config)]},key);
 assert.throws(()=>restoreCheckpoint(bad,key));
});
