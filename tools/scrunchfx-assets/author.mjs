// Scrunch-owned offline crumple authoring. No runtime solver or borrowed geometry.
// Units: rest sheet = 1, x right, y down, z toward viewer. The regular UV
// lattice permits exact geometry-field reflection without reflecting ink.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const out = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../src/Scrunch/Assets/ScrunchFX');
const check = process.argv.includes('--check');
function write(name,bytes) {
  const file=path.join(out,name);
  if(check) { if(!fs.readFileSync(file).equals(Buffer.from(bytes))) throw new Error('Non-reproducible asset: '+name); }
  else fs.writeFileSync(file,bytes);
}
// NFX2 stores samples as IEEE binary16. Math.fround first so the engine and the
// fallback converter round one float32 value once: rounding a double twice can
// disagree by a bit, and the assets must stay byte-reproducible on any host.
const halfView = typeof Float16Array === 'function' ? new Float16Array(1) : null;
const halfBits = halfView ? new Uint16Array(halfView.buffer) : null;
const single = new Float32Array(1), singleBits = new Uint32Array(single.buffer);
function encodeHalf(value) {
  single[0]=value; const x=singleBits[0];
  const sign=(x>>>16)&0x8000, exponent=(x>>>23)&0xff;
  let mantissa=x&0x7fffff;
  if(exponent===0xff) return sign|0x7c00|(mantissa?0x200:0); // infinity or quiet NaN
  let e=exponent-112; // drop the 127 bias, add 15
  if(e>=0x1f) return sign|0x7c00;
  if(e<=0) { // subnormal half or zero, round to nearest even
    if(e<-11) return sign;
    if(exponent!==0) mantissa|=0x800000;
    const shift=14-e, m=mantissa>>>shift, rest=mantissa&((1<<shift)-1), tie=1<<(shift-1);
    return sign|(rest>tie||(rest===tie&&(m&1))?m+1:m);
  }
  let m=mantissa>>>13; const rest=mantissa&0x1fff;
  if(rest>0x1000||(rest===0x1000&&(m&1))) { if(++m===0x400) { m=0; if(++e>=0x1f) return sign|0x7c00; } }
  return sign|(e<<10)|m;
}
function half(value) {
  const rounded=Math.fround(value);
  if(!halfBits) return encodeHalf(rounded);
  halfView[0]=rounded; return halfBits[0];
}
if(halfBits) for(let i=0;i<4096;i++) { // fail loudly instead of emitting other bytes
  const v=Math.fround(Math.sin(i*.37)*Math.pow(2,(i%44)-22));
  halfView[0]=v;
  if(halfBits[0]!==encodeHalf(v)) throw new Error('Float16Array disagrees with the fallback half converter');
}
const side = 25, frames = 61, count = side * side;
const smooth = x => { x = Math.max(0, Math.min(1, x)); return x*x*(3-2*x); };
// Each family: the rest point the hand closes on first, how late (as a fraction
// of the crumple) the gather reaches the farthest point, an optional thumb that
// presses the start point back and holds it, and the seed for the paper's flaws.
const families = {
  'corner-crush': {from:[.5,.5],wave:.3,seed:11},
  'side-scrunch': {from:[.5,0],wave:.3,seed:23},
  'centre-collapse': {from:[0,0],wave:.15,poke:{depth:-.12,width:.22,until:.4},seed:37}
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
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const unit=a=>{const n=Math.hypot(...a)||1;return a.map(v=>v/n);};
if(!check) fs.mkdirSync(out,{recursive:true});
const rest=new Float64Array(count*3);
for(let i=0;i<count;i++) { rest[i*3]=uv[i*2]-.5; rest[i*3+1]=uv[i*2+1]-.5; }
const settings={substeps:20,iterations:6,thickness:.009,bendStiffness:.2,yieldAngle:.3,damping:.04,settle:.9,grip:.5,imperfection:.01,
  depth:.35,finalRadius:[.155,.155,.12],squeeze:.85,ease:1.5,release:.15,lump:.15,finalLump:.4,lumpFrequency:2.5};
const report={generator:'Scrunch confined-sheet crumple v3',
  generatorSha256:crypto.createHash('sha256').update(fs.readFileSync(fileURLToPath(import.meta.url),'utf8').replace(/\r\n/g,'\n')).digest('hex'),
  format:'NFX2',sampleType:'float16',sampleLayout:'per frame, per vertex: position xyz then normal xyz',
  side,frames,vertices:count,triangles:indices.length/3,panelJitter:.32,settings,families:[]};
// The sheet is inextensible edges joined by plastic hinges, solved with
// position-based dynamics. The hand is a confining ellipsoid per vertex: it
// starts at the vertex's rest radius and closes to the wad once the gather wave
// reaches that vertex, so the paper itself decides where it folds.
function crumple(family) {
  const S=settings,n=count,x=Float64Array.from(rest),v=new Float64Array(n*3),p=new Float64Array(n*3),h=S.thickness,F=S.finalRadius;
  let state=family.seed>>>0; const random=()=>(state=(Math.imul(state,1664525)+1013904223)>>>0)/4294967296;
  const waves=Array.from({length:5},()=>[(random()-.5)*14,(random()-.5)*14,random()*6.2832]);
  // No sheet is truly flat. Smooth rest-angle flaws let buckles grow gradually
  // instead of the flat sheet storing compression and then snapping.
  const restAngle=new Float64Array(hinges.length/4),flaw=new Float64Array(hinges.length/4),contact=new Uint8Array(n);
  for(let q=0;q<hinges.length;q+=4) { const mx=(rest[hinges[q]*3]+rest[hinges[q+1]*3])/2,my=(rest[hinges[q]*3+1]+rest[hinges[q+1]*3+1])/2;
    flaw[q/4]=S.imperfection*waves.reduce((s,[a,b,c])=>s+Math.sin(a*mx+b*my+c),0); }
  // Low-frequency bumps on the hand keep the wad's outline from reading as a sphere.
  const lumps=Array.from({length:6},()=>{const z=random()*2-1,a=random()*6.2832,r=Math.sqrt(1-z*z);return [r*Math.cos(a)*S.lumpFrequency,r*Math.sin(a)*S.lumpFrequency,z*S.lumpFrequency,random()*6.2832];});
  const reach=new Float64Array(n),onset=new Float64Array(n); let far=0;
  for(let i=0;i<n;i++) { reach[i]=Math.hypot(rest[i*3],rest[i*3+1]); onset[i]=Math.hypot(rest[i*3]-family.from[0],rest[i*3+1]-family.from[1]); far=Math.max(far,onset[i]); }
  for(let i=0;i<n;i++) onset[i]=onset[i]/far*family.wave;
  const samples=[Float64Array.from(rest)];
  for(let f=1;f<frames;f++) for(let s=0;s<S.substeps;s++) {
    const t=(f-1+(s+1)/S.substeps)/(frames-1),flawed=smooth(t/.2),lump=S.lump*smooth(t/.3)*(1-smooth(t)*(1-S.finalLump));
    // After the squeeze the hand eases open so each fold's elastic part springs back into facets, then the wad settles.
    const opening=smooth((t-S.squeeze)/(1-S.squeeze)),open=1+S.release*opening,drag=S.damping+(S.settle-S.damping)*opening;
    for(let i=0;i<n*3;i++) { v[i]*=1-drag; p[i]=x[i]+v[i]; }
    if(family.poke) { const k=smooth(t/family.poke.until);
      for(let i=0;i<n;i++) { const d=Math.hypot(rest[i*3]-family.from[0],rest[i*3+1]-family.from[1]),w=Math.exp(-d*d/(family.poke.width**2));
        p[i*3+2]+=(family.poke.depth*k*w-p[i*3+2])*w*.2; } }
    const pairs=collisions(x,p,h),near=crossings(x,p,h);
    for(let it=0;it<S.iterations;it++) {
      for(let i=0;i<n;i++) {
        const k=smooth((t-onset[i])/(S.squeeze-onset[i]))**S.ease,R=reach[i]+(F[0]*open-reach[i])*k,Ry=reach[i]+(F[1]*open-reach[i])*k,Rz=Math.min(reach[i],S.depth)+(F[2]*open-Math.min(reach[i],S.depth))*k;
        if(!R) continue; // the centre point has nothing to confine until the gather reaches it
        const o=i*3,qx=p[o]/R,qy=p[o+1]/Ry,qz=p[o+2]/Rz,m=Math.hypot(qx,qy,qz);
        let bump=0; if(k>0&&lump>0) for(const l of lumps) bump+=Math.sin((l[0]*qx+l[1]*qy+l[2]*qz)/m+l[3]);
        const limit=1+lump*k*bump/3;
        if(m>limit) { p[o]=qx*R*limit/m; p[o+1]=qy*Ry*limit/m; p[o+2]=qz*Rz*limit/m; contact[i]=1; }
      }
      for(let q=0;q<pairs.length;q+=2) separate(p,pairs[q],pairs[q+1],h);
      for(let q=0;q<near.length;q+=9) apart(p,near,q,h);
      for(let q=0;q<hinges.length;q+=4) bend(p,q,restAngle[q/4]+flaw[q/4]*flawed,S.bendStiffness);
      for(let e=0;e<edges.length;e+=2) {
        const a=edges[e]*3,b=edges[e+1]*3,dx=p[b]-p[a],dy=p[b+1]-p[a+1],dz=p[b+2]-p[a+2],l=Math.hypot(dx,dy,dz)||1e-12,k=(l-edgeRest[e/2])/l*.5;
        p[a]+=dx*k;p[a+1]+=dy*k;p[a+2]+=dz*k;p[b]-=dx*k;p[b+1]-=dy*k;p[b+2]-=dz*k;
      }
      // Contacts again last, so the stretch pass cannot leave one layer pulled through another.
      for(let q=0;q<pairs.length;q+=2) separate(p,pairs[q],pairs[q+1],h);
      for(let q=0;q<near.length;q+=9) apart(p,near,q,h);
    }
    for(let i=0;i<n;i++) for(let k=0;k<3;k++) { const o=i*3+k; v[o]=(p[o]-x[o])*(contact[i]?1-S.grip:1); x[o]=p[o]; }
    contact.fill(0);
    // Plastic hinges: bending past the yield angle moves the rest angle, so creases stay.
    for(let q=0;q<hinges.length;q+=4) { const j=q/4,d=wrap(hinge(x,q)[8]-restAngle[j]-flaw[j]*flawed);
      if(Math.abs(d)>S.yieldAngle) restAngle[j]=wrap(restAngle[j]+(d-Math.sign(d)*S.yieldAngle)); }
    if(s===S.substeps-1) samples.push(Float64Array.from(x));
  }
  return samples;
}
const wrap=a=>a>Math.PI?a-2*Math.PI:a<-Math.PI?a+2*Math.PI:a;
// Unique edges with rest lengths, and hinges [edge0, edge1, wing1, wing2] with the edge running as in wing1's triangle.
const edges=[],edgeRest=[],hinges=[];
{ const seen=new Map();
  for(let t=0;t<indices.length;t+=3) for(let k=0;k<3;k++) {
    const a=indices[t+k],b=indices[t+(k+1)%3],w=indices[t+(k+2)%3],key=Math.min(a,b)*count+Math.max(a,b),other=seen.get(key);
    if(other) hinges.push(other[0],other[1],other[2],w);
    else { seen.set(key,[a,b,w]); edges.push(a,b); edgeRest.push(Math.hypot(rest[a*3]-rest[b*3],rest[a*3+1]-rest[b*3+1])); }
  } }
// Hinge vectors, area-weighted wing normals and the signed dihedral angle (zero when flat).
function hinge(p,q) {
  const e0=hinges[q]*3,e1=hinges[q+1]*3,w1=hinges[q+2]*3,w2=hinges[q+3]*3;
  const E=[p[e1]-p[e0],p[e1+1]-p[e0+1],p[e1+2]-p[e0+2]],A=[p[w1]-p[e0],p[w1+1]-p[e0+1],p[w1+2]-p[e0+2]],B=[p[w1]-p[e1],p[w1+1]-p[e1+1],p[w1+2]-p[e1+2]];
  const C=[p[w2]-p[e1],p[w2+1]-p[e1+1],p[w2+2]-p[e1+2]],D=[p[w2]-p[e0],p[w2+1]-p[e0+1],p[w2+2]-p[e0+2]];
  const n1=cross(A,B),n2=cross(C,D),m=cross(n1,n2),l=Math.hypot(...E);
  return [E,A,B,C,D,n1,n2,l,Math.atan2((m[0]*E[0]+m[1]*E[1]+m[2]*E[2])/l,n1[0]*n2[0]+n1[1]*n2[1]+n1[2]*n2[2])];
}
// Dihedral angle constraint (Muller et al. 2007) using the analytic angle
// gradient of Bridson et al. 2003, which stays signed so folds keep a side.
function bend(p,q,target,stiffness) {
  const [E,A,B,C,D,n1,n2,l,angle]=hinge(p,q),a1=n1[0]**2+n1[1]**2+n1[2]**2,a2=n2[0]**2+n2[1]**2+n2[2]**2;
  if(a1<1e-14||a2<1e-14||l<1e-9) return;
  const s1=(B[0]*E[0]+B[1]*E[1]+B[2]*E[2])/l,s2=(C[0]*E[0]+C[1]*E[1]+C[2]*E[2])/l,t1=(A[0]*E[0]+A[1]*E[1]+A[2]*E[2])/l,t2=(D[0]*E[0]+D[1]*E[1]+D[2]*E[2])/l;
  const g=[[],[],[],[]];
  for(let k=0;k<3;k++) { g[2][k]=-n1[k]*l/a1; g[3][k]=-n2[k]*l/a2; g[0][k]=-(s1*n1[k]/a1+s2*n2[k]/a2); g[1][k]=t1*n1[k]/a1+t2*n2[k]/a2; }
  let sum=0; for(const d of g) sum+=d[0]**2+d[1]**2+d[2]**2;
  const lambda=-wrap(angle-target)*stiffness/sum,ids=[hinges[q]*3,hinges[q+1]*3,hinges[q+2]*3,hinges[q+3]*3];
  for(let j=0;j<4;j++) for(let k=0;k<3;k++) p[ids[j]+k]+=g[j][k]*lambda;
}
// Paper has thickness (Bridson et al. 2002): vertex-triangle pairs found near
// each other at the start of a substep remember which side the vertex was on,
// and the solver keeps it at least the thickness away on that side.
function collisions(x,p,h) {
  const cell=.06,grid=new Map(),key=(i,j,k)=>(i+512)+1024*((j+512)+1024*(k+512)),pairs=[],box=new Float64Array(indices.length*2);
  for(let t=0;t<indices.length;t+=3) {
    const lo=[1e9,1e9,1e9],hi=[-1e9,-1e9,-1e9],b=t*2;
    for(let a=0;a<3;a++) { box[b+a]=1e9; box[b+3+a]=-1e9; }
    for(let k=0;k<3;k++) { const o=indices[t+k]*3; for(let a=0;a<3;a++) { lo[a]=Math.min(lo[a],x[o+a],p[o+a]); hi[a]=Math.max(hi[a],x[o+a],p[o+a]); box[b+a]=Math.min(box[b+a],x[o+a]); box[b+3+a]=Math.max(box[b+3+a],x[o+a]); } }
    // A foot point with barycentrics above -.1 lies within a fifth of the box's extent of it.
    for(let a=0;a<3;a++) { const slack=3*h+.2*(box[b+3+a]-box[b+a]); box[b+a]-=slack; box[b+3+a]+=slack; }
    for(let i=Math.floor((lo[0]-2*h)/cell);i<=Math.floor((hi[0]+2*h)/cell);i++) for(let j=Math.floor((lo[1]-2*h)/cell);j<=Math.floor((hi[1]+2*h)/cell);j++)
      for(let k=Math.floor((lo[2]-2*h)/cell);k<=Math.floor((hi[2]+2*h)/cell);k++) { const c=key(i,j,k); let list=grid.get(c); if(!list) grid.set(c,list=[]); list.push(t); }
  }
  for(let v=0;v<count;v++) {
    const X=x[v*3],Y=x[v*3+1],Z=x[v*3+2],list=grid.get(key(Math.floor(X/cell),Math.floor(Y/cell),Math.floor(Z/cell))); if(!list) continue;
    for(const t of list) {
      const a=indices[t],b=indices[t+1],c=indices[t+2],o=t*2; if(a===v||b===v||c===v) continue;
      if(X<box[o]||X>box[o+3]||Y<box[o+1]||Y>box[o+4]||Z<box[o+2]||Z>box[o+5]) continue;
      const hit=project(x,v,a,b,c); if(hit&&Math.abs(hit[0])<3*h&&hit[1]>-.1&&hit[2]>-.1&&hit[3]>-.1) pairs.push(v,hit[0]<0?-t-1:t+1);
    }
  }
  return pairs;
}
// Signed distance of vertex v above triangle (a,b,c), the barycentric weights of its foot point, and the unit normal.
function project(p,v,a,b,c) {
  const A=a*3,B=b*3,C=c*3,V=v*3,e1=[p[B]-p[A],p[B+1]-p[A+1],p[B+2]-p[A+2]],e2=[p[C]-p[A],p[C+1]-p[A+1],p[C+2]-p[A+2]];
  const nn=cross(e1,e2),area=Math.hypot(...nn); if(area<1e-12) return null;
  const w=[p[V]-p[A],p[V+1]-p[A+1],p[V+2]-p[A+2]],d=(w[0]*nn[0]+w[1]*nn[1]+w[2]*nn[2])/area;
  const u=cross(w,e2),z=cross(e1,w),beta=(u[0]*nn[0]+u[1]*nn[1]+u[2]*nn[2])/(area*area),gamma=(z[0]*nn[0]+z[1]*nn[1]+z[2]*nn[2])/(area*area);
  return [d,1-beta-gamma,beta,gamma,nn.map(q=>q/area)];
}
function separate(p,v,code,h) {
  const t=Math.abs(code)-1,side=code<0?-1:1,a=indices[t],b=indices[t+1],c=indices[t+2],hit=project(p,v,a,b,c);
  if(!hit||hit[1]<-.05||hit[2]<-.05||hit[3]<-.05) return;
  const gap=side*hit[0]-h; if(gap>=0) return;
  const n=hit[4],wts=[hit[1],hit[2],hit[3]],lambda=-gap/(1+wts[0]**2+wts[1]**2+wts[2]**2);
  for(let k=0;k<3;k++) { p[v*3+k]+=side*n[k]*lambda; p[a*3+k]-=side*n[k]*lambda*wts[0]; p[b*3+k]-=side*n[k]*lambda*wts[1]; p[c*3+k]-=side*n[k]*lambda*wts[2]; }
}
// Edge-edge pairs near each other at the start of a substep keep their separating direction, so crossing edges are caught too.
function crossings(x,p,h) {
  const cell=.06,grid=new Map(),key=(i,j,k)=>(i+512)+1024*((j+512)+1024*(k+512)),found=[],m=edges.length/2,box=new Float64Array(m*6),cells=new Int32Array(m*6);
  for(let e=0;e<m;e++) {
    const a=edges[e*2]*3,b=edges[e*2+1]*3;
    for(let k=0;k<3;k++) { box[e*6+k]=Math.min(x[a+k],x[b+k],p[a+k],p[b+k])-h; box[e*6+3+k]=Math.max(x[a+k],x[b+k],p[a+k],p[b+k])+h; cells[e*6+k]=Math.floor(box[e*6+k]/cell); cells[e*6+3+k]=Math.floor(box[e*6+3+k]/cell); }
    for(let i=cells[e*6];i<=cells[e*6+3];i++) for(let j=cells[e*6+1];j<=cells[e*6+4];j++) for(let k=cells[e*6+2];k<=cells[e*6+5];k++) { const c=key(i,j,k); let list=grid.get(c); if(!list) grid.set(c,list=[]); list.push(e); }
  }
  for(let e=0;e<m;e++) for(let i=cells[e*6];i<=cells[e*6+3];i++) for(let j=cells[e*6+1];j<=cells[e*6+4];j++) for(let k=cells[e*6+2];k<=cells[e*6+5];k++) for(const g of grid.get(key(i,j,k))) {
    // Each overlapping pair is tested once, in the first cell the two boxes share.
    if(g<=e||i!==Math.max(cells[e*6],cells[g*6])||j!==Math.max(cells[e*6+1],cells[g*6+1])||k!==Math.max(cells[e*6+2],cells[g*6+2])) continue;
    if(box[e*6]>box[g*6+3]||box[g*6]>box[e*6+3]||box[e*6+1]>box[g*6+4]||box[g*6+1]>box[e*6+4]||box[e*6+2]>box[g*6+5]||box[g*6+2]>box[e*6+5]) continue;
    const a=edges[e*2],b=edges[e*2+1],c=edges[g*2],d=edges[g*2+1];
    if(a===c||a===d||b===c||b===d) continue;
    const hit=closest(x,a,b,c,d); if(hit&&hit[0]<2*h) found.push(a,b,c,d,hit[1],hit[2],hit[3],hit[4],hit[5]);
  }
  return found;
}
// Distance between the interior closest points of edges ab and cd, their parameters, and the unit direction from ab to cd.
function closest(p,a,b,c,d) {
  const A=a*3,B=b*3,C=c*3,D=d*3,u=[p[B]-p[A],p[B+1]-p[A+1],p[B+2]-p[A+2]],v=[p[D]-p[C],p[D+1]-p[C+1],p[D+2]-p[C+2]],w=[p[A]-p[C],p[A+1]-p[C+1],p[A+2]-p[C+2]];
  const uu=u[0]*u[0]+u[1]*u[1]+u[2]*u[2],uv=u[0]*v[0]+u[1]*v[1]+u[2]*v[2],vv=v[0]*v[0]+v[1]*v[1]+v[2]*v[2],uw=u[0]*w[0]+u[1]*w[1]+u[2]*w[2],vw=v[0]*w[0]+v[1]*w[1]+v[2]*w[2],den=uu*vv-uv*uv;
  if(den<1e-12*uu*vv) return null; // parallel edges are left to the vertex-triangle test
  const s=(uv*vw-vv*uw)/den,t=(uu*vw-uv*uw)/den; if(s<=0||s>=1||t<=0||t>=1) return null;
  const n=[0,1,2].map(k=>p[C+k]+v[k]*t-p[A+k]-u[k]*s),l=Math.hypot(...n); if(l<1e-12) return null;
  return [l,s,t,n[0]/l,n[1]/l,n[2]/l];
}
function apart(p,list,q,h) {
  const a=list[q]*3,b=list[q+1]*3,c=list[q+2]*3,d=list[q+3]*3,s=list[q+4],t=list[q+5],n=[list[q+6],list[q+7],list[q+8]];
  let gap=-h; for(let k=0;k<3;k++) gap+=(p[c+k]*(1-t)+p[d+k]*t-p[a+k]*(1-s)-p[b+k]*s)*n[k];
  if(gap>=0) return;
  const lambda=-gap/((1-s)**2+s*s+(1-t)**2+t*t);
  for(let k=0;k<3;k++) { p[a+k]-=n[k]*lambda*(1-s); p[b+k]-=n[k]*lambda*s; p[c+k]+=n[k]*lambda*(1-t); p[d+k]+=n[k]*lambda*t; }
}
for(const [name,family] of Object.entries(families)) {
  const data=new Uint16Array(count*frames*6); let finalBounds;
  const samples=crumple(family);
  for(let f=0;f<frames;f++) {
    const s=samples[f],positions=Array.from({length:count},(_,i)=>[s[i*3],s[i*3+1],s[i*3+2]]);
    const normals=positions.map(()=>[0,0,0]);
    for(let t=0;t<indices.length;t+=3) {
      const [a,b,c]=indices.slice(t,t+3),n=cross(sub(positions[b],positions[a]),sub(positions[c],positions[a]));
      for(const v of [a,b,c]) for(let k=0;k<3;k++) normals[v][k]+=n[k];
    }
    for(let i=0;i<count;i++) {
      const p=positions[i],n=unit(normals[i]),o=(f*count+i)*6;
      for(let k=0;k<3;k++) { data[o+k]=half(p[k]); data[o+3+k]=half(n[k]); }
    }
    if(f===frames-1) finalBounds=[0,1,2].map(k=>Math.max(...positions.map(p=>p[k]))-Math.min(...positions.map(p=>p[k])));
  }
  const header=Buffer.alloc(16);header.write('NFX2');header.writeUInt32LE(count,4);header.writeUInt32LE(frames,8);header.writeUInt32LE(indices.length,12);
  const bytes=Buffer.concat([header,Buffer.from(new Float32Array(uv).buffer),Buffer.from(new Uint32Array(indices).buffer),Buffer.from(data.buffer)]);
  write(name+'.nfx',bytes);
  report.families.push({name,...family,bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),finalBounds});
  console.log(name,bytes.length,finalBounds);
}
write('scrunch-provenance.json',JSON.stringify(report,null,2)+'\n');
