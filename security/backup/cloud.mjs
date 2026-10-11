// Trusted operator/CI entry point. Never run with production credentials on a PR ref.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { open, seal, entry, restore, limit } from './archive.mjs';

export const sha256=bytes=>createHash('sha256').update(bytes).digest('hex');
export function verifyRemoteCopy(original,downloaded,key) {
    if(sha256(original)!==sha256(downloaded))throw new Error('Remote archive differs from uploaded archive.');
    const payload=open(downloaded,key);
    if(payload.dataSource!=='railway-volume-export'||! /^[a-f0-9]{40}$/.test(payload.revision))throw new Error('Unexpected recovery manifest.');
    for(const name of ['App_Data/station-status.json','App_Data/starlink-status.json']) {
        const file=payload.files.find(file=>file.name===name);
        if(!file)throw new Error('Missing production snapshot.');
        JSON.parse(Buffer.from(file.data,'base64').toString('utf8'));
    }
    return payload;
}
export function checkpoint(config,key) {
    return seal({version:1,dataSource:'onedrive-credential-checkpoint',files:[entry('rclone.conf',config)]},key);
}
export function restoreCheckpoint(bytes,key) {
    const payload=open(bytes,key);
    if(payload.dataSource!=='onedrive-credential-checkpoint'||payload.files.length!==1||payload.files[0].name!=='rclone.conf')throw new Error('Unexpected credential checkpoint.');
    return Buffer.from(payload.files[0].data,'base64');
}

export async function runCloudBackup({now=new Date(),remoteType='onedrive'}={}) {
    const env=process.env;
    for(const name of ['RAILWAY_API_TOKEN','PETABIT_BACKUP_KEY_BASE64','PETABIT_ONEDRIVE_CONFIG','GITHUB_REPOSITORY','GITHUB_RUN_ID','RUNNER_TEMP','GH_TOKEN'])
        if(!env[name])throw new Error(`Missing required configuration: ${name}.`);
    if(env.GITHUB_REF!=='refs/heads/master'||!['schedule','workflow_dispatch'].includes(env.GITHUB_EVENT_NAME))throw new Error('Production backup requires a trusted master run.');
    const key=Buffer.from(env.PETABIT_BACKUP_KEY_BASE64,'base64');
    if(key.length!==32)throw new Error('Recovery key must decode to exactly 32 bytes.');
    if(!/^\d+$/.test(env.GITHUB_RUN_ID)||!/^[\w.-]+\/[\w.-]+$/.test(env.GITHUB_REPOSITORY))throw new Error('Invalid CI identity.');
    const repo=process.cwd(), root=fs.mkdtempSync(path.join(env.RUNNER_TEMP,'petabit-backup-'));
    const config=path.join(root,'rclone.conf'), keyFile=path.join(root,'backup.recovery-key');
    const stateFile=path.join(env.RUNNER_TEMP,'onedrive-state.pbit');
    const exportDir=path.join(root,'App_Data');
    const sshDir=path.join(root,'home','.ssh');fs.mkdirSync(sshDir,{recursive:true,mode:0o700});
    const identity=path.join(sshDir,'id_ed25519');
    const childEnv={...env,HOME:path.dirname(sshDir)};
    for(const name of ['RAILWAY_API_TOKEN','RAILWAY_TOKEN','GH_TOKEN','GITHUB_TOKEN','PETABIT_BACKUP_KEY_BASE64','PETABIT_ONEDRIVE_CONFIG'])delete childEnv[name];
    // Tokens are passed only through environment/private files. Do not echo subprocess output.
    const run=(tool,args,options={})=>{
        const toolEnv={...childEnv};
        if(tool==='railway')Object.assign(toolEnv,{
            RAILWAY_API_TOKEN:env.RAILWAY_API_TOKEN,
            RAILWAY_PROJECT_ID:'20c1e1d1-4daf-4012-b3ab-29cdf8abdca9',
            RAILWAY_ENVIRONMENT_ID:'f5334489-9c09-4614-b3aa-1d33c6543f02',
            RAILWAY_SERVICE_ID:'9957dae9-1a2b-47e3-9316-b180923b05f1'
        });
        if(tool==='gh')toolEnv.GH_TOKEN=env.GH_TOKEN;
        try{return execFileSync(tool,args,{cwd:repo,env:toolEnv,stdio:['ignore','pipe','pipe'],timeout:300000,maxBuffer:16*1024*1024,...options});}
        catch {throw new Error(`${path.basename(tool)} operation failed; credentials and raw output are withheld.`);}
    };
    fs.writeFileSync(keyFile,key,{flag:'wx',mode:0o600});
    let configReady=false, fingerprint, registrationAttempted=false;
    try {
        const history=JSON.parse(run('gh',['run','list','--repo',env.GITHUB_REPOSITORY,'--workflow','production-backup.yml','--branch','master','--status','completed','--limit','20','--json','databaseId,event']));
        let recovered=false;
        for(const previous of history.filter(item=>['schedule','workflow_dispatch'].includes(item.event))) {
            const artifacts=JSON.parse(run('gh',['api',`repos/${env.GITHUB_REPOSITORY}/actions/runs/${previous.databaseId}/artifacts`])).artifacts;
            if(!artifacts.some(item=>item.name==='onedrive-refresh-state'&&!item.expired))continue;
            const prior=path.join(root,'previous-state');fs.mkdirSync(prior,{mode:0o700});
            run('gh',['run','download',String(previous.databaseId),'--repo',env.GITHUB_REPOSITORY,'--name','onedrive-refresh-state','--dir',prior]);
            fs.writeFileSync(config,restoreCheckpoint(fs.readFileSync(path.join(prior,'onedrive-state.pbit')),key),{flag:'wx',mode:0o600});
            recovered=true;break;
        }
        if(!recovered)fs.writeFileSync(config,env.PETABIT_ONEDRIVE_CONFIG,{flag:'wx',mode:0o600});
        const section=fs.readFileSync(config,'utf8').split(/^\[petabit\][ \t]*\r?$/m)[1]?.split(/^\[/m)[0];
        if(!section||/^type\s*=\s*([\w-]+)[ \t]*\r?$/m.exec(section)?.[1]!==remoteType)throw new Error('The [petabit] remote must use the expected OneDrive backend.');
        configReady=true;
        // Explicit environment targeting avoids `link`'s personal-account discovery query,
        // which workspace-scoped API tokens cannot authorize.
        console.log('Registering temporary Railway SSH access.');
        run('ssh-keygen',['-t','ed25519','-N','','-C',`petabit-backup-${env.GITHUB_RUN_ID}`,'-f',identity]);
        fingerprint=run('ssh-keygen',['-lf',identity+'.pub']).toString().split(/\s+/)[1];
        if(!/^SHA256:[A-Za-z0-9+/]+$/.test(fingerprint))throw new Error('Unexpected SSH fingerprint.');
        registrationAttempted=true;
        run('railway',['ssh','keys','add','--key',`petabit-backup-${env.GITHUB_RUN_ID}`,'--name',`petabit-backup-${env.GITHUB_RUN_ID}`]);
        try {
            console.log('Exporting the production volume.');
            run('railway',['volume','files','--volume','d11b1d77-4679-4df3-b0f5-d00411a76ddb','download','/',exportDir,'--json']);
        } finally {
            run('railway',['ssh','keys','remove',fingerprint]);registrationAttempted=false;
        }
        console.log('Temporary Railway SSH access revoked; encrypting the backup.');
        for(const name of ['station-status.json','starlink-status.json']) {
            const file=path.join(exportDir,name);
            if(fs.statSync(file).size>12*1024*1024)throw new Error('Snapshot exceeds expected size.');
            JSON.parse(fs.readFileSync(file,'utf8'));
        }
        const date=now.toISOString().slice(0,10);
        const filename=`petabit-${date}-${env.GITHUB_RUN_ID}-production.pbit`;
        const archive=path.join(root,filename);
        run(process.execPath,[fileURLToPath(new URL('./backup.mjs',import.meta.url)),'create','--repository',repo,'--data',exportDir,'--data-source','railway-volume-export','--key',keyFile,'--output',archive]);
        const remote=`petabit:Petabit Backups/Automated/${filename}`;
        const rclone=(...args)=>run('rclone',['--config',config,'--contimeout','20s','--timeout','2m','--retries','3',...args]);
        console.log('Uploading the encrypted archive to OneDrive.');
        rclone('copyto',archive,remote,'--immutable');
        const readback=path.join(root,'readback.pbit');
        rclone('copyto',remote,readback,'--max-transfer',`${limit}B`,'--cutoff-mode','HARD');
        if(fs.statSync(readback).size>limit)throw new Error('Remote archive exceeds backup limit.');
        const payload=verifyRemoteCopy(fs.readFileSync(archive),fs.readFileSync(readback),key);
        const restored=path.join(root,'restored');restore(payload,restored);
        const source=path.join(restored,'source');
        run('git',['clone',path.join(restored,'repository.bundle'),source]);
        run('git',['-C',source,'checkout','--detach',payload.revision]);
        run('git',['-C',source,'fsck','--full']);
        const monthDay=new Intl.DateTimeFormat('en-GB',{timeZone:'Europe/Zagreb',day:'2-digit'}).format(now);
        if(monthDay==='01'||env.PETABIT_RESTORE_DRILL==='true') {
            console.log('Verifying the restored application in an isolated environment.');
            run(process.execPath,[fileURLToPath(new URL('./drill.mjs',import.meta.url)),restored],{timeout:900000});
        }
        const receipt={date,revision:payload.revision,source:payload.dataSource,remote,sha256:sha256(fs.readFileSync(archive)),remoteReadbackVerified:true,gitRestoreVerified:true,fullApplicationDrill:monthDay==='01'||env.PETABIT_RESTORE_DRILL==='true',temporarySshKeyRevoked:true};
        fs.writeFileSync(path.join(env.RUNNER_TEMP,'backup-receipt.json'),JSON.stringify(receipt,null,2),{flag:'wx',mode:0o600});
        console.log('Encrypted production backup uploaded, downloaded and restored successfully.');
    } finally {
        // Preserve rotated OAuth tokens encrypted, including when a later backup operation fails.
        try {if(configReady)fs.writeFileSync(stateFile,checkpoint(fs.readFileSync(config),key),{flag:'wx',mode:0o600});}
        finally {
            try {if(registrationAttempted&&fingerprint)run('railway',['ssh','keys','remove',fingerprint]);}
            finally {for(const file of [identity,identity+'.pub',keyFile,config])if(fs.existsSync(file))fs.unlinkSync(file);}
        }
        // The GitHub-hosted runner is disposable; no plaintext exports are uploaded as artifacts.
    }
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))runCloudBackup().catch(error=>{console.error(error.message);process.exitCode=1;});
