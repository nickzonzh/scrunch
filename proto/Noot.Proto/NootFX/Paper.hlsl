// Mesh deformation, rigid motion, lighting and lifecycle are independent.
cbuffer Settings : register(b0) {
    float4 viewport; // width, height, paper width, paper height (physical px)
    float4 playback; // frame, vertex count, throw progress, alpha
    float4 paperColour;
    float4 origin; // paper centre x/y, deformation, reserved
    float4 variation; // direction, travel, rotation, vertical departure
    float4 orientation; // geometry signs x/y, square lattice side, reserved
};
StructuredBuffer<float4> bake : register(t0); // position, normal per point/frame
StructuredBuffer<float2> uvTable : register(t1);
Texture2D note : register(t2);
SamplerState linearClamp : register(s0);
Texture2D<float> foldDepth : register(t3);
SamplerComparisonState lightCompare : register(s1);
Texture2D scene : register(t4);
struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; float3 world : TEXCOORD1; };
static const float3 light = normalize(float3(-.45,-.6,1));
float3 throwOffset(float t) {
    // Scale with the captured physical paper, preserving motion at different DPI.
    float reach=sqrt(viewport.z*viewport.w)*variation.y;
    return float3(reach*t*variation.x,reach*(variation.w*t+.65*t*t),-reach*.7*t);
}
float3 rotate(float3 p, float t) {
    float a=t*variation.z*variation.x, b=t*.24*variation.x;
    p.xy=float2(cos(a)*p.x-sin(a)*p.y,sin(a)*p.x+cos(a)*p.y);
    p.xz=float2(cos(b)*p.x+sin(b)*p.z,-sin(b)*p.x+cos(b)*p.z);
    return p;
}
void paperVertex(uint id, out float3 p, out float3 n) {
    uint f=(uint)playback.x, count=(uint)playback.y;
    // Reflect the deformation FIELD and its vector, not the note UV. At rest
    // F'(u,v)=M F(M(u,v)) returns the original, readable sheet exactly.
    uint side=(uint)orientation.z, x=id%side, y=id/side;
    x=orientation.x<0?side-1-x:x; y=orientation.y<0?side-1-y:y;
    id=y*side+x;
    uint a=(f*count+id)*2, b=((f+1)*count+id)*2;
    float areaSize=sqrt(viewport.z*viewport.w);
    // Preserve the exact rectangular handoff, then gather the long axis more
    // deeply. A wide sticky should finish as compact paper, not a flat oval.
    float gather=smoothstep(.15,1,origin.z)*.85;
    float3 scale=float3(lerp(viewport.zw,areaSize.xx,gather),areaSize);
    p=lerp(bake[a].xyz,bake[b].xyz,frac(playback.x))*scale;
    n=normalize(lerp(bake[a+1].xyz,bake[b+1].xyz,frac(playback.x))/scale);
    p.xy*=orientation.xy; n.xy*=orientation.xy;
    float t=playback.z;
    p=rotate(p,t); n=rotate(n,t);
    p+=throwOffset(t);
}
float3 lightPosition(float3 p) {
    float3 right=normalize(cross(float3(0,1,0),light));
    float3 up=cross(light,right);
    float span=max(viewport.z,viewport.w)*1.6;
    return float3(dot(p,right)/span+.5, .5-dot(p,up)/span, .5-dot(p,light)/span);
}
float4 VSLight(uint id : SV_VertexID) : SV_POSITION {
    float3 p,n; paperVertex(id,p,n);
    float3 q=lightPosition(p);
    return float4(q.x*2-1,1-q.y*2,q.z,1);
}
Vertex VS(uint id : SV_VertexID) {
    float3 p,n; paperVertex(id,p,n);
    Vertex o; o.uv=uvTable[id]; o.normal=n; o.world=p;
    // Camera at +Z. Flat paper maps exactly to the captured pixel rectangle.
    float distance=max(viewport.z,viewport.w)*3.5;
    float perspective=distance/(distance-p.z);
    float2 screen=p.xy*perspective+origin.xy;
    // Preserve perspective-correct texture interpolation across folded triangles.
    float w=(distance-p.z)/distance;
    o.position=float4((screen.x/viewport.x*2-1)*w,(1-screen.y/viewport.y*2)*w,saturate(.5-p.z/(distance*2))*w,w);
    return o;
}
float4 PS(Vertex i, bool front : SV_IsFrontFace) : SV_TARGET {
    float3 n=normalize(i.normal);
    float3 facet=normalize(cross(ddx(i.world),ddy(i.world)));
    facet*=dot(facet,n)<0?-1:1;
    // Retain broad panel lighting but let intersecting creases read crisply.
    float crease=smoothstep(.015,.24,1-abs(dot(n,facet)));
    n=normalize(lerp(n,facet,(.22+.22*crease)*origin.z));
    // Orient toward the eye for double-sided paper, keep the reverse unprinted.
    float facing=n.z; n*=facing<0?-1:1;
    float3 printed=note.Sample(linearClamp,i.uv).rgb;
    float3 base=facing>0?printed:paperColour.rgb;
    // Prepared normals point toward the eye for the printed face at rest.
    // Broad matte response: fill grazing planes without erasing crease normals.
    float diffuse=saturate((dot(n,light)+.18)/1.18);
    float3 q=lightPosition(i.world);
    float visibility=0;
    [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
        visibility+=foldDepth.SampleCmpLevelZero(lightCompare,q.xy+float2(x,y)/1024.0,saturate(q.z-.0012));
    visibility/=9;
    // Paper scatters light into opposing folds. Keep the occlusion cue, but do
    // not extinguish the entire directional term inside the compact wad.
    float lighting=.66+.34*diffuse*lerp(.72,1,visibility);
    lighting=lerp(1,lighting,saturate(origin.z*5));
    float3 rgb=base*lighting;
    // Keep folds opaque to one another; opacity is applied once after resolving.
    return float4(rgb,1);
}
Vertex VSShadow(uint id : SV_VertexID) {
    float2 corners[6]={float2(-1,-1),float2(1,-1),float2(-1,1),float2(-1,1),float2(1,-1),float2(1,1)};
    float2 q=corners[id];
    float d=origin.z, t=playback.z;
    float2 compact=lerp(viewport.zw,sqrt(viewport.z*viewport.w).xx,.85);
    float2 radius=lerp(viewport.zw*.54,compact*float2(.28,.21),d);
    float2 screen=origin.xy+float2(7,13)+throwOffset(t).xy+q*radius;
    Vertex o; o.position=float4(screen.x/viewport.x*2-1,1-screen.y/viewport.y*2,.99,1);
    o.uv=q; o.normal=0; o.world=0; return o;
}
float4 PSShadow(Vertex i) : SV_TARGET {
    float d=lerp(max(abs(i.uv.x),abs(i.uv.y)),length(i.uv),saturate(origin.z*4));
    float alpha=(1-smoothstep(.5,1,d))*.15;
    return float4(0,0,0,alpha);
}
Vertex VSComposite(uint id : SV_VertexID) {
    Vertex o=(Vertex)0;
    o.uv=float2((id<<1)&2,id&2);
    o.position=float4(o.uv.x*2-1,1-o.uv.y*2,0,1);
    return o;
}
float4 PSComposite(Vertex i) : SV_TARGET { return scene.Sample(linearClamp,i.uv)*playback.w; }
