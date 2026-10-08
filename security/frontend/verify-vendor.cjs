const fs = require('node:fs');
const path = require('node:path');
const installed = path.join(__dirname, 'node_modules', 'satellite.js', 'dist');
const vendored = path.resolve(__dirname, '../../Petabit/wwwroot/lib/satellite-js/dist');
let count = 0;
function verify(relative = '') {
    for (const entry of fs.readdirSync(path.join(installed, relative), { withFileTypes: true })) {
        const file = path.join(relative, entry.name);
        if (entry.isDirectory()) verify(file);
        else {
            if (!fs.readFileSync(path.join(installed, file)).equals(fs.readFileSync(path.join(vendored, file))))
                throw new Error(`Vendored satellite.js differs from the integrity-verified npm package: ${file}`);
            count++;
        }
    }
}
verify();
console.log(`Verified ${count} vendored satellite.js distribution files.`);
