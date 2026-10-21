import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { createConsoleServer } from './experiment-console.mts';

async function fixture(t:any) {
  const directory=mkdtempSync(join(tmpdir(),'wheelchair-console-'));let now=100000;
  const app=createConsoleServer({dataDirectory:directory,port:0,now:()=>now});const address:any=await app.listen();const url='http://127.0.0.1:'+address.port;
  const page=await fetch(url);const html=await page.text();const cookie=page.headers.get('set-cookie')!.split(';')[0];const csrf=/name="csrf" content="([a-f0-9]+)"/.exec(html)![1];
  const api=async(path:string,body?:any,extra:any={})=>fetch(url+path,{method:body===undefined?'GET':'POST',headers:{cookie,'X-CSRF-Token':csrf,'Content-Type':'application/json',...extra},body:body===undefined?undefined:JSON.stringify(body)});
  const id='0123456789abcdef0123456789abcdef',code='765432';
  const hello=()=>fetch(url+'/device/hello',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({deviceId:id,codeHash:createHash('sha256').update(id+':'+code).digest('hex')})});
  t.after(async()=>{await app.close();rmSync(directory,{recursive:true,force:true});});
  return {directory,url,api,hello,id,code,advance:(ms:number)=>{now+=ms;},pair:async()=>{await hello();assert.equal((await api('/api/pair',{deviceId:id,code})).status,200);return (await (await hello()).json()).pairToken as string;},html};
}
test('local console pairs only the matching headset and serves offline assets',async t=>{
  const f=await fixture(t);assert.match(f.html,/被験者へ渡す前/);assert.equal((await f.api('/api/pair',{deviceId:f.id,code:f.code})).status,400);
  await f.hello();assert.equal((await f.api('/api/pair',{deviceId:f.id,code:'000000'})).status,400);assert.ok(await f.pair());
  assert.equal((await fetch(f.url+'/console.js')).status,200);assert.equal((await fetch(f.url+'/console.css')).status,200);
  assert.equal((await fetch(f.url+'/api/state')).status,401);
  assert.equal((await f.api('/api/heartbeat',{}, {'X-CSRF-Token':'wrong'})).status,403);
  assert.equal((await f.api('/api/heartbeat',{}, {origin:'https://example.com'})).status,403);
});
test('expired browser leases and old queued commands cannot drive the headset',async t=>{
  const f=await fixture(t);const token=await f.pair();const exchange=async(result?:any)=>(await fetch(f.url+'/device/exchange',{method:'POST',headers:{'X-Device-Token':token,'Content-Type':'application/json'},body:JSON.stringify({state:{visitId:'v',revision:1,running:false},result})})).json();
  await f.api('/api/heartbeat',{});await exchange();
  const command=await f.api('/api/command',{action:'start',visitId:'v',revision:1});assert.equal(command.status,202);const id=(await command.json()).id;
  assert.equal((await f.api('/api/command',{action:'start',visitId:'v',revision:1})).status,409);
  assert.equal((await exchange()).command.id,id);assert.equal((await exchange({id,ok:true})).command,null);
  await f.api('/api/command',{action:'start',visitId:'v',revision:1});f.advance(3001);
  const stale=await exchange();assert.equal(stale.operatorConnected,false);assert.equal(stale.command,null);
  assert.equal((await f.api('/api/command',{action:'start',visitId:'v',revision:1})).status,409);
  assert.equal((await fetch(f.url+'/device/exchange',{method:'POST',headers:{'X-Device-Token':'bad'},body:'{}'})).status,401);
});
test('record transfer verifies checksums, keeps originals and removes incomplete copies',async t=>{
  const f=await fixture(t);const token=await f.pair();await f.api('/api/heartbeat',{});
  const data=Buffer.from('PK\x03\x04test-archive');const digest=createHash('sha256').update(data).digest('hex');
  const upload=(sha:string)=>fetch(f.url+'/device/export',{method:'PUT',headers:{'X-Device-Token':token,'X-Content-SHA256':sha,'X-Export-Name':'WheelchairResearch-test.zip'},body:data});
  assert.equal((await upload('a'.repeat(64))).status,400);assert.deepEqual(readdirSync(f.directory),[]);
  assert.equal((await upload(digest)).status,200);const state=await(await f.api('/api/state')).json();assert.equal(state.archives[0].sha256,digest);
  const archive=await f.api('/api/archive/'+state.archives[0].id);assert.deepEqual(Buffer.from(await archive.arrayBuffer()),data);
  assert.deepEqual(data,Buffer.from('PK\x03\x04test-archive'));
  assert.equal(readdirSync(f.directory).filter(p=>p.endsWith('.partial')).length,0);
});
