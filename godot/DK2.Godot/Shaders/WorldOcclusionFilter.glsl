#[compute]
#version 450
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
layout(local_size_x=8, local_size_y=8, local_size_z=1) in;
layout(set=0,binding=0) uniform sampler2D depth_texture;
layout(set=0,binding=1) uniform sampler2D ao_texture;
layout(rgba16f,set=0,binding=2) uniform image2D color_texture;
layout(push_constant,std430) uniform Params { mat4 inverse_projection; vec2 size; vec2 projection_scale; } params;
vec3 view_at(ivec2 pixel) {
 vec2 uv=(vec2(pixel)+.5)/params.size;
 vec4 v=params.inverse_projection*vec4(uv*2.-1.,texelFetch(depth_texture,pixel,0).r,1.);
 return v.xyz/v.w;
}
void main() {
 ivec2 pixel=ivec2(gl_GlobalInvocationID.xy);
 if(any(greaterThanEqual(pixel,ivec2(params.size))))return;
 vec4 centre=texelFetch(ao_texture,pixel,0);
 if(dot(centre.xyz,centre.xyz)<.5)return;
 vec3 p=view_at(pixel);float sum=0.,weights=0.;
 for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++) {
  ivec2 q=clamp(pixel+ivec2(x,y),ivec2(0),ivec2(params.size)-1);
  vec4 tap=texelFetch(ao_texture,q,0);
  if(dot(tap.xyz,tap.xyz)<.5)continue;
  vec3 delta=view_at(q)-p;
  float normal_weight=pow(max(0.,dot(centre.xyz,tap.xyz)),16.);
  float plane=max(abs(dot(delta,centre.xyz)),abs(dot(delta,tap.xyz)));
  float w=exp(-float(x*x+y*y)/4.)*normal_weight*exp(-plane*plane/.0004);
  sum+=tap.w*w;weights+=w;
 }
 float ao=sum/max(weights,.0001);
#ifdef AO_UNFILTERED
 ao=centre.w;
#endif
 vec4 color=imageLoad(color_texture,pixel);
 imageStore(color_texture,pixel,vec4(color.rgb*(1.-.6*ao),color.a));
}
