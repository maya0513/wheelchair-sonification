import { createHash } from 'node:crypto';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
export function runtimeFingerprint(root:string){
  const files:string[]=[];
  const walk=(dir:string)=>{if(!existsSync(dir))return;for(const item of readdirSync(dir,{withFileTypes:true})){const p=join(dir,item.name);if(item.isDirectory())walk(p);else if(item.name.endsWith('.cs'))files.push(p);}};
  for(const folder of ['Core','Runtime'])walk(join(root,'wheelchair-sonification/Assets/Experiment',folder));
  const server=join(root,'scripts/experiment-console.mts');if(existsSync(server))files.push(server);
  const hash=createHash('sha256');for(const path of files.sort()){hash.update(relative(root,path).replaceAll('\\','/'));hash.update('\0');hash.update(readFileSync(path));}
  return hash.digest('hex');
}
export function completedSoak(root:string){
  const base=join(root,'.local');const fingerprint=runtimeFingerprint(root);
  const reports=existsSync(base)?readdirSync(base).filter(n=>n.startsWith('console-integration-')).sort().reverse():[];
  for(const name of reports){const path=join(base,name,'result.json');if(!existsSync(path))continue;const r=JSON.parse(readFileSync(path,'utf8'));
    if(r.passed===true&&r.synthetic===true&&r.physicalDeviceMeasured===false&&r.realElapsedSeconds>=5400&&r.requestedSeconds>=5400&&r.finished===true&&r.archives?.length>0&&r.runtimeSourceFingerprint===fingerprint)
      return {report:relative(root,path),realElapsedSeconds:r.realElapsedSeconds,runtimeSourceFingerprint:fingerprint,physicalDeviceMeasured:false};
  }
  throw new Error('現行の計測・進行・通信コードで完了した90分のUnity＋PC通し試験がありません。just experiment-soakを実行し、完了・回収を確認してください。');
}
