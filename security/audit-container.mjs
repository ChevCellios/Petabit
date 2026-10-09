import fs from 'node:fs';
const report=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
if (!Array.isArray(report.Results)) throw new Error('Missing Trivy scan results.');
const all=report.Results.flatMap(result=>result.Vulnerabilities||[]);
const high=all.filter(v=>['HIGH','CRITICAL'].includes(v.Severity));
console.log(JSON.stringify({total:all.length,highCritical:high.length,fixableHighCritical:high.filter(v=>v.FixedVersion).length,unfixedHighCritical:high.filter(v=>!v.FixedVersion).map(v=>({id:v.VulnerabilityID,package:v.PkgName,severity:v.Severity}))},null,2));
// Unfixed findings remain visible in the archived report; never silently suppress them.
if(high.length) { console.error('High/critical container vulnerabilities block release, including unfixed findings.');process.exitCode=1; }
