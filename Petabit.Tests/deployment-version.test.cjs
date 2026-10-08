const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {spawnSync} = require('node:child_process');
const expected = '0503a11029edfbac3e0e6394563b4ca84764f250';
const old = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
function run(responses, sha = expected, health = '200') {
 const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'petabit-version-'));
 try {
  fs.writeFileSync(path.join(dir, 'responses'), responses.join('\n')+'\n');
  fs.writeFileSync(path.join(dir, 'curl'), `#!/usr/bin/env bash
case "\${*: -1}" in
 */health/live) printf '%s' "$TEST_HEALTH" ;;
 */version)
  count=$(cat "$TEST_DIR/count" 2>/dev/null || echo 0)
  count=$((count+1))
  echo "$count" > "$TEST_DIR/count"
  sed -n "\${count}p" "$TEST_DIR/responses"
 ;;
 *) exit 2 ;;
esac
`, {mode:0o700});
  return spawnSync('bash', ['.github/scripts/wait-for-deployed-version.sh'], {
   encoding:'utf8',timeout:5000,
   env:{...process.env,PATH:dir+path.delimiter+process.env.PATH,TEST_DIR:dir,TEST_HEALTH:health,SITE_URL:'https://test.invalid',EXPECTED_SHA:sha,MAX_ATTEMPTS:String(responses.length),RETRY_SECONDS:'0'}
  });
 } finally { fs.rmSync(dir,{recursive:true,force:true}); }
}
test('deployment gate waits through old build and unavailable endpoint before accepting expected build',()=>{
 const r=run([JSON.stringify({commitSha:old}),'not JSON',JSON.stringify({commitSha:expected})]);
 assert.equal(r.status,0,r.stderr);assert.match(r.stdout,/on attempt 3/);
});
test('deployment gate rejects old build, missing metadata and malformed response',()=>{
 for(const body of [JSON.stringify({commitSha:old}),JSON.stringify({commitSha:null}),JSON.stringify({}),'<html>old deployment</html>']) {
  const r=run([body]);assert.equal(r.status,1,r.stdout+r.stderr);
 }
});
test('deployment gate never passes when liveness is unavailable',()=>{
 const r=run([JSON.stringify({commitSha:expected})],expected,'503');assert.equal(r.status,1);
});
test('deployment gate rejects abbreviated expected SHA',()=>{
 const r=run([JSON.stringify({commitSha:expected})],'0503a110');assert.equal(r.status,1);assert.match(r.stdout,/full lowercase Git SHA/);
});
