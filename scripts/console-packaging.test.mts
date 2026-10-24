import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync,mkdtempSync,readFileSync,writeFileSync,rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join,resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { packageConsole,packagePreviewPair } from './console-packaging.mts';

test('operator distribution contains only the runtime and offline frontend, never keys or research records',t=>{
 const output=mkdtempSync(join(tmpdir(),'console-package-'));t.after(()=>rmSync(output,{recursive:true,force:true}));
 const file=join(output,'operator.zip');packageConsole(resolve('.'),file,'a'.repeat(40),'0.3.0');
 const inspect=spawnSync('python3',['-c','import sys,zipfile,json\nwith zipfile.ZipFile(sys.argv[1]) as z:\n print(json.dumps({"files":z.namelist(),"start":z.read("start.sh").decode(),"info":json.loads(z.read("console-build-info.json"))}))',file],{encoding:'utf8'});
 assert.equal(inspect.status,0,inspect.stderr);const result=JSON.parse(inspect.stdout);
 assert.equal(result.files.length,9);assert.equal(result.info.sourceCommit,'a'.repeat(40));assert.match(result.start,/node scripts\/experiment-console.mts/);
 assert.ok(result.files.every((p:string)=>!p.includes('.local')&&!p.endsWith('.keystore')&&!p.includes('ResearchSessions')&&!p.endsWith('.test.mts')));
});
test('combined image archive includes both actual viewers and excludes fixtures and private subdirectories',t=>{
 const root=mkdtempSync(join(tmpdir(),'combined-preview-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
 for(const dir of ['headset','operator']){const path=join(root,dir);mkdirSync(join(path,'fixture-records'),{recursive:true});writeFileSync(join(path,'image.png'),'png fixture only');writeFileSync(join(path,'index.html'),'viewer');writeFileSync(join(path,'manifest.json'),'{}');writeFileSync(join(path,'fixture-records/poses.csv'),'private fixture');}
 const archive=join(root,'preview.zip');packagePreviewPair(join(root,'headset'),join(root,'operator'),archive);
 const output=spawnSync('python3',['-c','import zipfile,sys,json\nwith zipfile.ZipFile(sys.argv[1]) as z: print(json.dumps(z.namelist()))',archive],{encoding:'utf8'});
 assert.equal(output.status,0,output.stderr);const names=JSON.parse(output.stdout);assert.ok(names.includes('headset/index.html'));assert.ok(names.includes('operator/image.png'));assert.ok(names.includes('index.html'));assert.ok(!names.some((n:string)=>n.includes('fixture-records')));
});
