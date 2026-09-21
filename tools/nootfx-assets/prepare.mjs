// Offline decoder only. No JavaScript or Three.js is shipped with Noot.
// Decoding conventions from item-develop/paper-crumple-demo (MIT; see asset LICENSE).
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { FloatType } from 'three';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';
import { EXRLoader } from 'three/examples/jsm/loaders/EXRLoader.js';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const source = path.join(root, 'proto/artifacts/nootfx-source');
const output = path.join(root, 'proto/Noot.Proto/Assets/NootFX');
const read = n => { const b = fs.readFileSync(path.join(source,n)); return b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength); };
const group = new FBXLoader().parse(read('paper.fbx'), '');
let geo; group.traverse(o => { if(o.isMesh && !geo) geo=o.geometry; });
const exr = new EXRLoader().setDataType(FloatType).parse(read('position.exr'));
const p=geo.attributes.position, uv=geo.attributes.uv, vat=geo.attributes.uv1;
const ids=new Map(), vertices=[], indices=[];
for(let t=0;t<p.count;t+=3) {
  if([t,t+1,t+2].some(v=>vat.getX(v)===0 && vat.getY(v)===0)) continue;
  for(let v=t;v<t+3;v++) {
    const id=Math.floor(vat.getX(v)*exr.width)+Math.floor((1-vat.getY(v))*exr.height)*exr.width;
    if(!ids.has(id)) {ids.set(id,vertices.length);vertices.push({id,rest:[p.getX(v),p.getY(v),p.getZ(v)],uv:[uv.getX(v),uv.getY(v)]});}
    indices.push(ids.get(id));
  }
}
const count=vertices.length, rows=Math.ceil((Math.max(...ids.keys())+1)/exr.width), channels=exr.data.length/(exr.width*exr.height);
const frames=[];
for(let f=0;f<50;f++) {
 const xyz=new Float32Array(count*3);
 vertices.forEach((v,i)=>{
  const row=50*rows-1-(f*rows+Math.floor(v.id/exr.width));
  const offset=(row*exr.width+v.id%exr.width)*channels;
  xyz.set([v.rest[0]-exr.data[offset],v.rest[1]+exr.data[offset+1],v.rest[2]+exr.data[offset+2]],i*3);
 }); frames.push(xyz);
}
const deltas=frames.slice(1).map((f,i)=>f.reduce((max,x,j)=>Math.max(max,Math.abs(x-frames[i][j])),0));
let first=deltas.findIndex(x=>x>1e-4),last=deltas.findLastIndex(x=>x>1e-4)+1;
const active=frames.slice(Math.max(0,first),last+1);
const flat=frames[0];
const bounds=[0,1,2].map(a=>[Math.min(...vertices.map((_,i)=>flat[i*3+a])),Math.max(...vertices.map((_,i)=>flat[i*3+a]))]);
// Canonical coordinates: x right, y down, z toward camera. Frame zero matches
// UVs exactly; original XZ plane is mapped using the UV orientation from the FBX.
const width=bounds[0][1]-bounds[0][0],height=bounds[2][1]-bounds[2][0];
const xSign=vertices.reduce((s,v,i)=>s+(v.uv[0]-.5)*flat[i*3],0)>0?1:-1;
const ySign=vertices.reduce((s,v,i)=>s+(.5-v.uv[1])*flat[i*3+2],0)>0?1:-1;
const data=new Float32Array(active.length*count*8);
active.forEach((frame,f)=>{
 const pos=vertices.map((v,i)=>[(frame[i*3]-(bounds[0][0]+bounds[0][1])/2)/width*xSign,(frame[i*3+2]-(bounds[2][0]+bounds[2][1])/2)/height*ySign,(frame[i*3+1]-bounds[1][0])/Math.sqrt(width*height)]);
 const normals=vertices.map(()=>[0,0,0]);
 for(let t=0;t<indices.length;t+=3){
  const [a,b,c]=indices.slice(t,t+3),e=pos[b].map((x,k)=>x-pos[a][k]),g=pos[c].map((x,k)=>x-pos[a][k]);
  const n=[e[1]*g[2]-e[2]*g[1],e[2]*g[0]-e[0]*g[2],e[0]*g[1]-e[1]*g[0]];
  for(const v of [a,b,c]) n.forEach((x,k)=>normals[v][k]+=x);
 }
 vertices.forEach((v,i)=>{const n=normals[i],len=Math.hypot(...n)||1; data.set([...pos[i],0,...n.map(x=>x/len),0],(f*count+i)*8);});
});
if(!Array.from(data).every(Number.isFinite)||count!==3500||indices.length!==20286) throw Error('Invalid bake');
const header=Buffer.alloc(16);header.write('NFX1');header.writeUInt32LE(count,4);header.writeUInt32LE(active.length,8);header.writeUInt32LE(indices.length,12);
const uvs=new Float32Array(vertices.flatMap(v=>[v.uv[0],1-v.uv[1]]));
const idx=new Uint32Array(indices);
fs.mkdirSync(output,{recursive:true});
const bytes=Buffer.concat([header,Buffer.from(uvs.buffer),Buffer.from(idx.buffer),Buffer.from(data.buffer)]);
fs.writeFileSync(path.join(output,'crumple.nfx'),bytes);
fs.copyFileSync(path.join(source,'LICENSE'),path.join(output,'LICENSE.txt'));
const report={vertices:count,triangles:indices.length/3,frames:active.length,firstSourceFrame:first,lastSourceFrame:last,bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),bounds,xSign,ySign,source:'https://github.com/item-develop/paper-crumple-demo',inputs:Object.fromEntries(['paper.fbx','position.exr'].map(n=>[n,crypto.createHash('sha256').update(fs.readFileSync(path.join(source,n))).digest('hex')]))};
fs.writeFileSync(path.join(output,'provenance.json'),JSON.stringify(report,null,2)+'\n');
console.log(report);
