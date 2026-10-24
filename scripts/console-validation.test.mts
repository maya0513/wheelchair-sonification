import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync,mkdtempSync,writeFileSync,rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { completedSoak,runtimeFingerprint } from './console-validation.mts';
test('optional soak evidence ignores short, failed and stale-source reports',t=>{
 const root=mkdtempSync(join(tmpdir(),'soak-report-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
 const dir=join(root,'.local/console-integration-fixture');mkdirSync(dir,{recursive:true});
 const source=join(root,'wheelchair-sonification/Assets/Experiment/Core');mkdirSync(source,{recursive:true});writeFileSync(join(source,'fixture.cs'),'fixture-only');
 const valid={passed:true,synthetic:true,physicalDeviceMeasured:false,realElapsedSeconds:5400,requestedSeconds:5400,finished:true,archives:[{id:'fixture'}],runtimeSourceFingerprint:runtimeFingerprint(root)};
 for(const change of [{realElapsedSeconds:5399},{passed:false},{finished:false},{archives:[]},{runtimeSourceFingerprint:'old'},{physicalDeviceMeasured:true}]){writeFileSync(join(dir,'result.json'),JSON.stringify({...valid,...change}));assert.equal(completedSoak(root).realElapsedSeconds,0);}
 writeFileSync(join(dir,'result.json'),JSON.stringify(valid));assert.equal(completedSoak(root).realElapsedSeconds,5400);
 writeFileSync(join(source,'fixture.cs'),'changed');assert.equal(completedSoak(root).realElapsedSeconds,0);
});
