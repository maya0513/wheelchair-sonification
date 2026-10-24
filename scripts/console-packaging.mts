import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
export function validateConsolePreview(directory:string,commit?:string){
  const manifest=JSON.parse(readFileSync(join(directory,'manifest.json'),'utf8'));
  if(!manifest.passed||manifest.physical_device_measured!==false||manifest.shots?.length<12||commit&&manifest.source_commit!==commit)throw new Error('実施者画面の画像・ソースを確認できません。');
  for(const shot of manifest.shots){if(!/^[a-zA-Z0-9_-]+\.png$/.test(shot.file))throw new Error('画像名が不正です。');const file=join(directory,shot.file);if(!existsSync(file)||statSync(file).size<100||readFileSync(file).subarray(0,8).toString('hex')!=='89504e470d0a1a0a')throw new Error('実施者画面のPNGがありません。');}
  return manifest;
}
export function packageConsole(root:string,output:string,commit:string,version:string){
  const files=['scripts/experiment-console.mts','scripts/console-demo.mts','scripts/console/index.html','scripts/console/console.js','scripts/console/console.css','scripts/console/timing.mjs'];
  for(const file of files)if(!existsSync(join(root,file)))throw new Error('操作コンソールのファイルがありません: '+file);
  const script=`import json,pathlib,sys,zipfile\nr=pathlib.Path(sys.argv[1])\nfiles=json.loads(sys.argv[3])\nwith zipfile.ZipFile(sys.argv[2],"x",zipfile.ZIP_DEFLATED) as z:\n for f in files:\n  p=r/f\n  if p.is_symlink(): raise ValueError("links are not packaged")\n  z.write(p,f)\n z.writestr("console-build-info.json",json.dumps({"sourceCommit":sys.argv[4],"version":sys.argv[5],"operatorProtocol":2,"requires":"Node.js 24"},indent=2))\n z.writestr("start.sh",'#!/usr/bin/env sh\\nset -eu\\ncd -- "$(dirname -- "$0")"\\nexec node scripts/experiment-console.mts\\n')\n z.writestr("README.md","# 実施者用コンソール\\n\\nNode.js 24を事前に導入し、実施者用Ubuntu PCでは sh start.sh で起動します。Windows PCはAPKのコピーに使用します。\\n先にPCのホットスポットへFocus Visionを接続してください。Wi-Fi設定はこのコマンドから変更しません。\\n操作ページは http://127.0.0.1:8765/ です。ゴーグルに表示された確認番号を入力して承認します。\\nPCでUnity・VIVE Hubや外部回線を常時使う必要はありません。\\n元データはゴーグルへ保存し、回収データはホーム内の ~/WheelchairSonificationData/ に保存します。\\n\\nSource: "+sys.argv[4]+"\\n")\n`;
  const r=spawnSync('python3',['-c',script,root,output,JSON.stringify(files),commit,version],{encoding:'utf8',shell:false});if(r.status!==0)throw new Error('操作コンソールを梱包できません: '+r.stderr);
}
export function packagePreviewPair(headset:string,operator:string,output:string){
  const script=`import pathlib,sys,zipfile\nwith zipfile.ZipFile(sys.argv[3],"x",zipfile.ZIP_DEFLATED) as z:\n for folder,prefix in [(sys.argv[1],"headset"),(sys.argv[2],"operator")]:\n  for p in sorted(pathlib.Path(folder).iterdir()):\n   if p.is_file() and not p.is_symlink() and (p.suffix in (".png",".wav") or p.name in ("index.html","manifest.json","audio-manifest.json")): z.write(p,prefix+"/"+p.name)\n z.writestr("index.html",'<!doctype html><html lang="ja"><meta charset="utf-8"><title>画面確認</title><h1>画面確認</h1><p><a href="headset/index.html">被験者用：Unityの描画</a></p><p><a href="operator/index.html">実施者用：Chromeの描画</a></p><p>合成入力での画面確認です。実機の光学系・パススルー・計測性能を再現した画像ではありません。</p></html>')\n`;
  const r=spawnSync('python3',['-c',script,headset,operator,output],{encoding:'utf8',shell:false});if(r.status!==0)throw new Error('画面一覧を梱包できません: '+r.stderr);
}

export function packageOverview(root:string,output:string,commit:string){
  const document=readFileSync(join(root,'docs/app-overview.md'),'utf8');
  const images=[...new Set([...document.matchAll(/\]\((images\/app-overview\/[A-Za-z0-9_-]+\.png)\)/g)].map(m=>m[1]))];
  if(!images.length)throw new Error('アプリ概要にテスト画像がありません。');
  const files=[...images,'images/app-overview/capture-info.json'];
  for(const file of files)if(!existsSync(join(root,'docs',file)))throw new Error('概要資料の画像・撮影情報がありません: '+file);
  // Keep images local in the ZIP; detailed documents remain readable through the immutable source commit.
  const portable=document.replace(/\]\(((?:development|specifications)\/[^)]+\.md(?:#[^)]*)?)\)/g,
    (_,path)=>`](https://github.com/maya0513/wheelchair-sonification/blob/${commit}/docs/${path})`);
  const script=`import json,pathlib,sys,zipfile\nr=pathlib.Path(sys.argv[1])/"docs"\nwith zipfile.ZipFile(sys.argv[2],"x",zipfile.ZIP_DEFLATED) as z:\n z.writestr("app-overview.md",sys.argv[4])\n for name in json.loads(sys.argv[3]):\n  p=r/name\n  if p.is_symlink(): raise ValueError("links are not packaged")\n  z.write(p,name)\n`;
  const r=spawnSync('python3',['-c',script,root,output,JSON.stringify(files),portable],{encoding:'utf8',shell:false});
  if(r.status!==0)throw new Error('アプリ概要を梱包できません: '+r.stderr);
}
