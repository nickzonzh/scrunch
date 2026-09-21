// Mesh deformation, rigid motion, lighting and lifecycle are independent.
cbuffer Settings : register(b0) {
    float4 viewport; // width, height, paper width, paper height (physical px)
    float4 playback; // frame, vertex count, throw progress, alpha
    float4 paperColour;
    float4 origin; // paper centre x/y, deformation, shadow pass
};
StructuredBuffer<float4> bake : register(t0); // position, normal per point/frame
StructuredBuffer<float2> uvTable : register(t1);
Texture2D note : register(t2);
SamplerState linearClamp : register(s0);
struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; float3 world : TEXCOORD1; };
float3 rotate(float3 p, float t) {
    float a=t*.62, b=t*.32;
    p.xy=float2(cos(a)*p.x-sin(a)*p.y,sin(a)*p.x+cos(a)*p.y);
    p.xz=float2(cos(b)*p.x+sin(b)*p.z,-sin(b)*p.x+cos(b)*p.z);
    return p;
}
Vertex VS(uint id : SV_VertexID) {
    uint f=(uint)playback.x, count=(uint)playback.y;
    uint a=(f*count+id)*2, b=((f+1)*count+id)*2;
    float3 scale=float3(viewport.z,viewport.w,sqrt(viewport.z*viewport.w));
    float3 p=lerp(bake[a].xyz,bake[b].xyz,frac(playback.x))*scale;
    float3 n=normalize(lerp(bake[a+1].xyz,bake[b+1].xyz,frac(playback.x))/scale);
    float t=playback.z;
    p=rotate(p,t); n=rotate(n,t);
    p+=float3(100*t,-45*sin(t*2.2)+65*t*t,-90*t);
    Vertex o; o.uv=uvTable[id]; o.normal=n; o.world=p;
    // Camera at +Z. Flat paper maps exactly to the captured pixel rectangle.
    float distance=max(viewport.z,viewport.w)*3.5;
    float perspective=distance/(distance-p.z);
    float2 screen=p.xy*perspective+origin.xy;
    if(origin.w>0) screen=p.xy*.98+origin.xy+float2(10,16)+float2(-.16,.24)*p.z;
    o.position=float4(screen.x/viewport.x*2-1,1-screen.y/viewport.y*2,saturate(.5-p.z/(distance*2)),1);
    return o;
}
float4 PS(Vertex i, bool front : SV_IsFrontFace) : SV_TARGET {
    float3 n=normalize(i.normal);
    // Orient toward the eye for double-sided paper, keep the reverse unprinted.
    float facing=n.z; n*=facing<0?-1:1;
    float3 printed=note.Sample(linearClamp,i.uv).rgb;
    float3 base=facing>0?printed:paperColour.rgb;
    // Prepared normals point toward the eye for the printed face at rest.
    float diffuse=saturate(dot(n,normalize(float3(-.45,-.6,1))));
    float lighting=.60+.42*diffuse;
    lighting=lerp(1,lighting,saturate(origin.z*5));
    float crease=pow(saturate(1-abs(n.z)),2)*.10*origin.z;
    float3 rgb=base*(lighting-crease);
    return float4(rgb*playback.w,playback.w);
}
Vertex VSShadow(uint id : SV_VertexID) {
    float2 corners[6]={float2(-1,-1),float2(1,-1),float2(-1,1),float2(-1,1),float2(1,-1),float2(1,1)};
    float2 q=corners[id];
    float d=origin.z, t=playback.z;
    float2 radius=viewport.zw*lerp(float2(.54,.54),float2(.28,.21),d);
    float2 screen=origin.xy+float2(7+100*t,13-45*sin(t*2.2)+65*t*t)+q*radius;
    Vertex o; o.position=float4(screen.x/viewport.x*2-1,1-screen.y/viewport.y*2,.99,1);
    o.uv=q; o.normal=0; o.world=0; return o;
}
float4 PSShadow(Vertex i) : SV_TARGET {
    float d=lerp(max(abs(i.uv.x),abs(i.uv.y)),length(i.uv),saturate(origin.z*4));
    float alpha=(1-smoothstep(.5,1,d))*.15*playback.w;
    return float4(0,0,0,alpha);
}
