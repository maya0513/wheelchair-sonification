import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync,mkdtempSync,readFileSync,writeFileSync,rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join,resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { packageConsole,packagePreviewPair,packageOverview } from './console-packaging.mts';

test('operator distribution contains only the runtime and offline frontend, never keys or research records',t=>{
 const output=mkdtempSync(join(tmpdir(),'console-package-'));t.after(()=>rmSync(output,{recursive:true,force:true}));
 const file=join(output,'operator.zip');packageConsole(resolve('.'),file,'a'.repeat(40),'0.3.0');
 const inspect=spawnSync('python3',['-c','import sys,zipfile,json\nwith zipfile.ZipFile(sys.argv[1]) as z:\n print(json.dumps({"files":z.namelist(),"start":z.read("start.sh").decode(),"info":json.loads(z.read("console-build-info.json"))}))',file],{encoding:'utf8'});
 assert.equal(inspect.status,0,inspect.stderr);const result=JSON.parse(inspect.stdout);
 assert.equal(result.files.length,10);assert.equal(result.info.operatorProtocol,3);assert.equal(result.info.sourceCommit,'a'.repeat(40));assert.match(result.start,/node scripts\/experiment-console.mts/);
 assert.ok(result.files.every((p:string)=>!p.includes('.local')&&!p.endsWith('.keystore')&&!p.includes('ResearchSessions')&&!p.endsWith('.test.mts')));
});
test('combined image archive includes both actual viewers and excludes fixtures and private subdirectories',t=>{
 const root=mkdtempSync(join(tmpdir(),'combined-preview-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
 for(const dir of ['headset','operator']){const path=join(root,dir);mkdirSync(join(path,'fixture-records'),{recursive:true});writeFileSync(join(path,'image.png'),'png fixture only');writeFileSync(join(path,'index.html'),'viewer');writeFileSync(join(path,'manifest.json'),'{}');writeFileSync(join(path,'fixture-records/poses.csv'),'private fixture');}
 const archive=join(root,'preview.zip');packagePreviewPair(join(root,'headset'),join(root,'operator'),archive);
 const output=spawnSync('python3',['-c','import zipfile,sys,json\nwith zipfile.ZipFile(sys.argv[1]) as z: print(json.dumps(z.namelist()))',archive],{encoding:'utf8'});
 assert.equal(output.status,0,output.stderr);const names=JSON.parse(output.stdout);assert.ok(names.includes('headset/index.html'));assert.ok(names.includes('operator/image.png'));assert.ok(names.includes('index.html'));assert.ok(!names.some((n:string)=>n.includes('fixture-records')));
});

test('overview ZIP keeps referenced images local and links detailed documents to the source commit',t=>{
 const root=mkdtempSync(join(tmpdir(),'overview-package-'));t.after(()=>rmSync(root,{recursive:true,force:true}));
 const folder=join(root,'docs/images/app-overview');mkdirSync(folder,{recursive:true});
 writeFileSync(join(root,'docs/app-overview.md'),'# 概要\n![テスト](images/app-overview/view.png)\n[全体](images/app-overview/full.png)\n[詳細](development/experiment-console.md)\n[仕様](specifications/feedback.md#提示する場面)\n');
 for(const name of ['view.png','full.png'])writeFileSync(join(folder,name),'image fixture');
 writeFileSync(join(folder,'capture-info.json'),'{}');writeFileSync(join(folder,'poses.csv'),'private');writeFileSync(join(folder,'unreferenced.png'),'not included');
 const output=join(root,'overview.zip');packageOverview(root,output,'a'.repeat(40));
 const inspect=spawnSync('python3',['-c','import sys,zipfile,json\nwith zipfile.ZipFile(sys.argv[1]) as z: print(json.dumps({"files":z.namelist(),"text":z.read("app-overview.md").decode()}))',output],{encoding:'utf8'});
 assert.equal(inspect.status,0,inspect.stderr);const result=JSON.parse(inspect.stdout);
 assert.equal(result.files.length,4);assert.ok(result.files.includes('images/app-overview/full.png'));
 assert.ok(!result.files.some((p:string)=>p.includes('poses.csv')||p.includes('unreferenced')));
 assert.match(result.text,/!\[テスト\]\(images\/app-overview\/view.png\)/);
 assert.ok(result.text.includes('blob/'+'a'.repeat(40)+'/docs/development/experiment-console.md'));
 assert.ok(result.text.includes('feedback.md#提示する場面'));
 rmSync(join(folder,'full.png'));assert.throws(()=>packageOverview(root,join(root,'missing.zip'),'a'.repeat(40)),/画像・撮影情報/);
});
