import fs from 'node:fs';
const report=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
if(!Array.isArray(report.site)||!report.site.length)throw new Error('Missing ZAP target results.');
const alerts=report.site.flatMap(site=>site.alerts||[]);
console.log(JSON.stringify(alerts.map(alert=>({id:alert.alertRef,name:alert.alert,risk:alert.riskdesc})),null,2));
if(alerts.some(alert=>Number(alert.riskcode)>=2)) {console.error('Medium/high passive security findings require review before release.');process.exitCode=1;}
