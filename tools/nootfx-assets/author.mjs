// Noot-owned offline fold authoring. No runtime solver or borrowed geometry.
// Units: rest sheet = 1, x right, y down, z toward viewer. The regular UV
// lattice permits exact geometry-field reflection without reflecting ink.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const out = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../proto/Noot.Proto/Assets/NootFX');
const check = process.argv.includes('--check');
function write(name,bytes) {
  const file=path.join(out,name);
  if(check) { if(!fs.readFileSync(file).equals(Buffer.from(bytes))) throw new Error('Non-reproducible asset: '+name); }
  else fs.writeFileSync(file,bytes);
}
const side = 25, frames = 61, count = side * side;
const smooth = x => { x = Math.max(0, Math.min(1, x)); return x*x*(3-2*x); };
// [normal angle, crease offset, signed hinge angle, onset, completion].
// Unequal intervals, oblique intersections and reversed folds avoid pleats.
const families = {
  'corner-crush': [
    [.76,.34,2.25,0,.28], [-.48,.19,-1.65,.13,.47], [2.1,.16,2.15,.25,.59],
    [-2.5,.12,2.35,.37,.71], [.26,.09,-1.7,.48,.82], [1.55,.02,1.8,.61,.94],
    [-.9,-.02,1.1,.76,1]],
  'side-scrunch': [
    [.14,.26,2.18,0,.30], [1.92,.27,-1.9,.12,.44], [-1.14,.20,2.25,.24,.57],
    [2.92,.13,2.3,.35,.70], [.88,.04,-1.75,.49,.83], [-.45,.03,2.45,.62,.94],
    [-2.0,-.04,2.1,.77,1]],
  'centre-collapse': [
    [1.17,-.03,-.55,.10,.36], [-.69,.13,1.95,.18,.47], [2.59,.21,2.35,.29,.61],
    [-1.68,.17,2.10,.36,.70], [.42,.08,-2.05,.48,.82], [1.96,.01,2.35,.61,.94],
    [-.26,-.03,1.8,.76,1]]
};
const uv = [], indices = [];
for(let y=0;y<side;y++) for(let x=0;x<side;x++) {
  // Static, reflection-symmetric irregular panel layout (no temporal noise).
  const ax=Math.min(x,side-1-x), ay=Math.min(y,side-1-y);
  const jx=x===0||x===side-1||x===(side-1)/2?0:Math.sin(ax*17.1+ay*5.7)*.32*Math.sign(x-(side-1)/2);
  const jy=y===0||y===side-1||y===(side-1)/2?0:Math.sin(ax*7.3+ay*13.1)*.32*Math.sign(y-(side-1)/2);
  uv.push((x+jx)/(side-1),(y+jy)/(side-1));
}
for(let y=0;y<side-1;y++) for(let x=0;x<side-1;x++) {
  const a=y*side+x,b=a+1,c=a+side,d=c+1;
  // Checkerboard diagonals remain reflection-compatible.
  if((x+y)%2) indices.push(a,b,c,b,d,c); else indices.push(a,b,d,a,d,c);
}
const sub=(a,b)=>a.map((v,i)=>v-b[i]);
const dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0);
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const unit=a=>{const n=Math.hypot(...a)||1;return a.map(v=>v/n);};
function rotate(p, origin, axis, angle) {
  const q=sub(p,origin), c=Math.cos(angle),s=Math.sin(angle),k=cross(axis,q),d=dot(axis,q);
  return q.map((v,i)=>origin[i]+v*c+k[i]*s+axis[i]*d*(1-c));
}
function pose(rest, folds, progress, hinges = []) {
  let p=[...rest];
  for(let j=0;j<folds.length;j++) {
    const [direction,offset,angle,start,end]=folds[j];
    const amount=smooth((progress-start)/(end-start)); if(amount===0) continue;
    const n=[Math.cos(direction),Math.sin(direction)];
    // Transform the hinge with the preceding folds: downstream folds belong
    // to the sheet, rather than remaining attached to the desktop.
    const centre=[n[0]*offset,n[1]*offset,0], tangent=[-n[1],n[0],0];
    const a=hinges[j]?.a ?? pose(centre,folds.slice(0,j),progress,hinges);
    const b=hinges[j]?.b ?? pose(centre.map((v,i)=>v+tangent[i]*.035),folds.slice(0,j),progress,hinges);
    const axis=unit(sub(b,a));
    const distance=rest[0]*n[0]+rest[1]*n[1]-offset;
    const weight=smooth(distance/.055);
    p=rotate(p,a,axis,angle*amount*weight);
  }
  return p;
}
if(!check) fs.mkdirSync(out,{recursive:true});
const edgeKeys=new Set(),edges=[];
for(let t=0;t<indices.length;t+=3) for(let k=0;k<3;k++) {
  const a=indices[t+k],b=indices[t+(k+1)%3],key=[Math.min(a,b),Math.max(a,b)].join(':');
  if(edgeKeys.has(key)) continue; edgeKeys.add(key);
  edges.push([a,b,Math.hypot(uv[a*2]-uv[b*2],uv[a*2+1]-uv[b*2+1])]);
}
const report={generator:'Noot staged oblique hinge folds with offline edge projection v1',
  generatorSha256:crypto.createHash('sha256').update(fs.readFileSync(fileURLToPath(import.meta.url),'utf8').replace(/\r\n/g,'\n')).digest('hex'),
  format:'NFX1',side,frames,vertices:count,triangles:indices.length/3,
  panelJitter:.32,projectionIterations:90,guideWeight:.035,edgeCorrection:.48,
  pressure:{onset:.38,xyz:[.59,.61,.57]},families:[]};
for(const [name,folds] of Object.entries(families)) {
  const data=new Float32Array(count*frames*8); let finalBounds;
  let previous=Array.from({length:count},(_,i)=>[uv[i*2]-.5,uv[i*2+1]-.5,0]);
  for(let f=0;f<frames;f++) {
    const progress=f/(frames-1);
    const hinges=[];
    for(let j=0;j<folds.length;j++) {
      const [angle,offset]=folds[j],n=[Math.cos(angle),Math.sin(angle)],c=[n[0]*offset,n[1]*offset,0];
      hinges.push({a:pose(c,folds.slice(0,j),progress,hinges),b:pose([c[0]-n[1]*.035,c[1]+n[0]*.035,0],folds.slice(0,j),progress,hinges)});
    }
    const positions=Array.from({length:count},(_,i)=>{
      const x=uv[i*2]-.5,y=uv[i*2+1]-.5;
      // A shallow off-centre dimple initiates the centre family before its
      // unequal corner folds arrive. Piecewise planar, never a radial wave.
      const cave=name==='centre-collapse'?-.24*smooth(progress/.25)*Math.max(0,1-Math.max(Math.abs(x-.045)*2.6,Math.abs(y+.035)*2.9)):0;
      return pose([x,y,cave],folds,progress,hinges);
    });
    // Follow the mass slowly; no sudden recentering at the last frame.
    const mean=[0,1,2].map(k=>positions.reduce((s,p)=>s+p[k],0)/count);
    const centre=smooth(progress)*.82;
    for(const p of positions) for(let k=0;k<3;k++) p[k]-=mean[k]*centre;
    // A closing palm compresses the already creased stack. Axis-specific
    // pressure keeps the mass irregular; this is not a sphere projection.
    const compression=smooth((progress-.38)/.62);
    for(const p of positions) { p[0]*=1-.59*compression; p[1]*=1-.61*compression; p[2]*=1-.57*compression; }
    if(f>0) {
      const target=positions.map(p=>[...p]);
      for(let i=0;i<count;i++) positions[i]=[...previous[i]];
      // Offline inextensibility projection, without collision or dynamics at
      // runtime. A weak moving guide drives the authored trajectory while
      // short panel edges resist the stretched spikes of direct hinge warps.
      for(let iteration=0;iteration<90;iteration++) {
        for(let i=0;i<count;i++) for(let k=0;k<3;k++) positions[i][k]+=(target[i][k]-positions[i][k])*.035;
        for(let e=0;e<edges.length;e++) {
          const [a,b,rest]=edges[iteration%2?edges.length-1-e:e],p=positions[a],q=positions[b];
          const delta=sub(q,p),length=Math.hypot(...delta)||1;
          const correction=(length-rest)/length*.48;
          for(let k=0;k<3;k++) { p[k]+=delta[k]*correction; q[k]-=delta[k]*correction; }
        }
      }
    }
    previous=positions.map(p=>[...p]);
    const normals=positions.map(()=>[0,0,0]);
    for(let t=0;t<indices.length;t+=3) {
      const [a,b,c]=indices.slice(t,t+3),n=cross(sub(positions[b],positions[a]),sub(positions[c],positions[a]));
      for(const v of [a,b,c]) for(let k=0;k<3;k++) normals[v][k]+=n[k];
    }
    for(let i=0;i<count;i++) data.set([...positions[i],0,...unit(normals[i]),0],(f*count+i)*8);
    if(f===frames-1) finalBounds=[0,1,2].map(k=>Math.max(...positions.map(p=>p[k]))-Math.min(...positions.map(p=>p[k])));
  }
  const header=Buffer.alloc(16);header.write('NFX1');header.writeUInt32LE(count,4);header.writeUInt32LE(frames,8);header.writeUInt32LE(indices.length,12);
  const bytes=Buffer.concat([header,Buffer.from(new Float32Array(uv).buffer),Buffer.from(new Uint32Array(indices).buffer),Buffer.from(data.buffer)]);
  write(name+'.nfx',bytes);
  report.families.push({name,folds,bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),finalBounds});
  console.log(name,bytes.length,finalBounds);
}
write('noot-provenance.json',JSON.stringify(report,null,2)+'\n');
