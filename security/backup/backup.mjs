#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { entry, seal, open, restore, limit } from './archive.mjs';
const [command,...args]=process.argv.slice(2);
const options={};
for(let i=0;i<args.length;i+=2) { if(!args[i]?.startsWith('--') || !args[i+1] || options[args[i]]) throw new Error('Use unique --option value arguments.'); options[args[i]]=args[i+1]; }
const required=name=>{if(!options[name]) throw new Error(`Missing ${name}.`);return path.resolve(options[name]);};
const within=(root,target)=>{const relative=path.relative(root,target);return relative==='' || (!relative.startsWith('..'+path.sep) && relative!=='..' && !path.isAbsolute(relative));};
const read=file=>{if(fs.statSync(file).size>limit)throw new Error('File exceeds backup limit.');return fs.readFileSync(file);};
try {
    if(command==='keygen') {
        const output=required('--key');
        fs.writeFileSync(output,randomBytes(32),{flag:'wx',mode:0o600});
        console.log('Recovery key created. Keep a separate offline copy; key contents are never printed.');
    } else if(command==='create') {
        const repo=required('--repository'), data=required('--data'), output=required('--output'), key=read(required('--key'));
        if(within(repo,required('--key')) || within(data,required('--key')) || within(data,output)) throw new Error('Keep recovery keys outside the repository/data and archives outside the data directory.');
        if(execFileSync('git',['-C',repo,'status','--porcelain']).toString().trim()) throw new Error('Commit or preserve working changes before backing up; only committed Git history is included.');
        const temp=fs.mkdtempSync(path.join(os.tmpdir(),'petabit-bundle-'));
        const bundle=path.join(temp,'repository.bundle');
        // Temporary directory is fresh; only our known bundle is removed in finally.
        try {
            execFileSync('git',['-C',repo,'bundle','create',bundle,'--all']);
            const files=[entry('repository.bundle',read(bundle))];
            let total=files[0].size;
            function collect(directory,prefix) {
                for(const item of fs.readdirSync(directory,{withFileTypes:true})) {
                    const source=path.join(directory,item.name), name=prefix+'/'+item.name;
                    if(item.isSymbolicLink()) throw new Error('Data directory contains a symbolic link.');
                    if(item.isDirectory()) collect(source,name);
                    else if(item.isFile()) { const bytes=read(source);total+=bytes.length;if(total>limit/2)throw new Error('Data exceeds supported backup size.');files.push(entry(name,bytes)); }
                    else throw new Error('Unsupported file type in data directory.');
                }
            }
            if(fs.lstatSync(data).isSymbolicLink() || !fs.statSync(data).isDirectory())throw new Error('Data must be a regular directory.');
            collect(data,'App_Data');
            const payload={version:1,createdAt:new Date().toISOString(),revision:execFileSync('git',['-C',repo,'rev-parse','HEAD']).toString().trim(),dataSource:options['--data-source']||'operator-supplied directory',files};
            const encrypted=seal(payload,key); open(encrypted,key);
            fs.writeFileSync(output,encrypted,{flag:'wx',mode:0o600});
            console.log(JSON.stringify({archive:output,revision:payload.revision,createdAt:payload.createdAt,files:files.length,dataSource:payload.dataSource,verified:true}));
        } finally { if(fs.existsSync(bundle))fs.unlinkSync(bundle);fs.rmdirSync(temp); }
    } else if(command==='verify' || command==='restore') {
        const payload=open(read(required('--archive')),read(required('--key')));
        if(command==='restore') restore(payload,required('--destination'));
        console.log(JSON.stringify({verified:true,revision:payload.revision,createdAt:payload.createdAt,dataSource:payload.dataSource,files:payload.files.length,restored:command==='restore'}));
    } else throw new Error('Commands: keygen, create, verify, restore. See disaster-recovery.md.');
} catch(error) { console.error(error.message);process.exitCode=1; }
