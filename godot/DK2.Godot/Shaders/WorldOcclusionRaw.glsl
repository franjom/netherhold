#[compute]
#version 450
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
layout(local_size_x=8, local_size_y=8, local_size_z=1) in;
layout(set=0,binding=0) uniform sampler2D depth_texture;
layout(rgba16f,set=0,binding=1) uniform writeonly image2D ao_texture;
layout(push_constant,std430) uniform Params { mat4 inverse_projection; vec2 size; vec2 projection_scale; } params;
const float RADIUS=.35, BIAS=.008, FAR=14.;
ivec2 pixel_at(vec2 uv) { return clamp(ivec2(floor(uv*params.size)),ivec2(0),ivec2(params.size)-1); }
vec3 view_at(ivec2 pixel) {
 pixel=clamp(pixel,ivec2(0),ivec2(params.size)-1);
 vec2 uv=(vec2(pixel)+.5)/params.size;
 vec4 v=params.inverse_projection*vec4(uv*2.-1.,texelFetch(depth_texture,pixel,0).r,1.);
 return v.xyz/v.w;
}
void main() {
 ivec2 pixel=ivec2(gl_GlobalInvocationID.xy);
 if(any(greaterThanEqual(pixel,ivec2(params.size)))) return;
 float depth=texelFetch(depth_texture,pixel,0).r;
 vec3 p=view_at(pixel);float eye=-p.z;vec3 n=vec3(0.);float occlusion=0.;
 if(depth>0. && eye<FAR) {
  vec3 right=view_at(pixel+ivec2(1,0))-p,left=p-view_at(pixel-ivec2(1,0));
  vec3 up=view_at(pixel+ivec2(0,1))-p,down=p-view_at(pixel-ivec2(0,1));
  // A clamped neighbour at the image border is the centre, not a surface derivative.
  vec3 dx=pixel.x==0?right:(pixel.x==int(params.size.x)-1?left:(abs(right.z)<abs(left.z)?right:left));
  vec3 dy=pixel.y==0?up:(pixel.y==int(params.size.y)-1?down:(abs(up.z)<abs(down.z)?up:down));
  vec3 gradient=cross(dx,dy);float gradient_squared=dot(gradient,gradient);
  if(gradient_squared<1e-12 || isnan(gradient_squared) || isinf(gradient_squared)) {
   imageStore(ao_texture,pixel,vec4(0.));return;
  }
  n=gradient*inversesqrt(gradient_squared);
  if(dot(n,p)>0.)n=-n;
  vec2 uv=(vec2(pixel)+.5)/params.size;
  vec2 radius=params.projection_scale*(RADIUS*.5/eye);
  float turn=6.2831853*fract(52.9829189*fract(dot(vec2(pixel)+.5,vec2(.06711056,.00583715))));
  float sum=0.;
  for(int i=0;i<10;i++) {
   float t=(float(i)+.5)/10.;float angle=float(i)*2.3999632+turn;
   vec3 v=view_at(pixel_at(uv+vec2(cos(angle),sin(angle))*radius*t))-p;
   float vv=dot(v,v),reach=max(0.,1.-vv/(RADIUS*RADIUS));
   sum+=max(0.,dot(v,n)-BIAS*eye)/(vv+.0001)*reach;
  }
  occlusion=clamp(sum*RADIUS/10.,0.,1.)*(1.-smoothstep(FAR*.7,FAR,eye));
 }
 imageStore(ao_texture,pixel,vec4(n,occlusion));
}
