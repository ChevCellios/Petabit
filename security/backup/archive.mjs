import { createCipheriv, createDecipheriv, createHash, randomBytes } from 'node:crypto';
import { gzipSync, gunzipSync } from 'node:zlib';
import fs from 'node:fs';
import path from 'node:path';
const magic = Buffer.from('PETABIT-BACKUP-1\n');
export const limit = 384 * 1024 * 1024;
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
export function validate(payload) {
    if (payload?.version !== 1 || !Array.isArray(payload.files) || !payload.files.length || payload.files.length > 10000)
        throw new Error('Invalid backup manifest.');
    const seen = new Set(); let total = 0;
    for (const file of payload.files) {
        if (typeof file.name !== 'string' || file.name.includes('\\') || file.name.includes(':') || file.name.includes('\0') || /[<>"|?*\x00-\x1f]/.test(file.name) || file.name.toLowerCase() === 'backup-manifest.json'
            || file.name.startsWith('/') || file.name.split('/').some(part => !part || part === '.' || part === '..'
                || /[. ]$/.test(part) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\.|$)/i.test(part))
            || seen.has(file.name.toLowerCase()) || typeof file.data !== 'string' || !/^[A-Za-z0-9+/]*={0,2}$/.test(file.data))
            throw new Error('Unsafe or duplicate backup path.');
        seen.add(file.name.toLowerCase());
        const bytes = Buffer.from(file.data, 'base64'); total += bytes.length;
        if (total > limit || hash(bytes) !== file.sha256 || bytes.length !== file.size) throw new Error('Backup integrity check failed.');
    }
    return payload;
}
export function entry(name, bytes) { return { name, size: bytes.length, sha256: hash(bytes), data: bytes.toString('base64') }; }
export function seal(payload, key) {
    if (key.length !== 32) throw new Error('A 32-byte recovery key is required.');
    validate(payload);
    const nonce = randomBytes(12), cipher = createCipheriv('aes-256-gcm', key, nonce);
    cipher.setAAD(magic);
    const encrypted = Buffer.concat([cipher.update(gzipSync(Buffer.from(JSON.stringify(payload)))), cipher.final()]);
    return Buffer.concat([magic, nonce, cipher.getAuthTag(), encrypted]);
}
export function open(bytes, key) {
    if (key.length !== 32 || bytes.length > limit || bytes.length < magic.length + 29 || !bytes.subarray(0,magic.length).equals(magic))
        throw new Error('Invalid encrypted backup or recovery key.');
    const start = magic.length, cipher = createDecipheriv('aes-256-gcm', key, bytes.subarray(start,start+12));
    cipher.setAAD(magic); cipher.setAuthTag(bytes.subarray(start+12,start+28));
    const plain = Buffer.concat([cipher.update(bytes.subarray(start+28)), cipher.final()]);
    return validate(JSON.parse(gunzipSync(plain,{maxOutputLength:limit}).toString('utf8')));
}
export function restore(payload, destination) {
    validate(payload);
    // A new directory prevents overwrite, symlink traversal and accidental production restores.
    const target = path.resolve(destination);
    fs.mkdirSync(target, { recursive: false, mode: 0o700 });
    for (const file of payload.files) {
        const output = path.join(target, ...file.name.split('/'));
        fs.mkdirSync(path.dirname(output), { recursive:true, mode:0o700 });
        fs.writeFileSync(output, Buffer.from(file.data,'base64'), { flag:'wx', mode:0o600 });
    }
    fs.writeFileSync(path.join(target,'backup-manifest.json'), JSON.stringify({...payload,files:payload.files.map(({data,...metadata})=>metadata)},null,2),{flag:'wx',mode:0o600});
}
