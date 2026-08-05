export { MAX_BLOBS, SCENE_FRAG } from './scene.ts';

/** Fullscreen triangle generated from gl_VertexID — no vertex buffers needed. */
export const VERT = `#version 300 es
out vec2 vUv;
void main(){
  vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
  vUv = p;
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

/** Bright-pass plus a separable blur; `uDir` selects the axis. */
export const BLUR_FRAG = `#version 300 es
precision highp float;
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uTex;
uniform vec2 uTexel;
uniform vec2 uDir;
uniform float uThreshold;   // < 0 disables the bright-pass (second axis)

vec3 tap(vec2 uv){
  vec3 c = texture(uTex, uv).rgb;
  if (uThreshold >= 0.0){
    float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
    c *= smoothstep(uThreshold, uThreshold + 0.6, l);
  }
  return c;
}

void main(){
  // 9-tap Gaussian collapsed to 5 bilinear samples.
  const float o[3] = float[3](0.0, 1.3846153846, 3.2307692308);
  const float w[3] = float[3](0.2270270270, 0.3162162162, 0.0702702703);
  vec3 sum = tap(vUv) * w[0];
  for (int i = 1; i < 3; i++){
    vec2 off = uDir * uTexel * o[i];
    sum += tap(vUv + off) * w[i];
    sum += tap(vUv - off) * w[i];
  }
  fragColor = vec4(sum, 1.0);
}`;

/** Final grade: bloom, chromatic aberration, tonemap, vignette, grain. */
export const COMPOSITE_FRAG = `#version 300 es
precision highp float;
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform vec2  uRes;
uniform float uTime;
uniform float uBloomStrength;
uniform float uChroma;      // radial chromatic aberration
uniform float uFlash;       // white/red hit flash
uniform vec3  uFlashCol;
uniform float uVignette;
uniform vec2  uShake;       // screen-space trauma offset, in uv units

vec3 aces(vec3 x){
  return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

void main(){
  // Shake is applied here rather than to the camera so it never desyncs the
  // simulation from what the player sees.
  vec2 uv = clamp(vUv + uShake, 0.0, 1.0);
  vec2 c = uv - 0.5;
  float r2 = dot(c, c);

  // Chromatic aberration scales with radius, so the centre stays sharp.
  float amt = uChroma * (0.004 + r2 * 0.016);
  vec3 col;
  col.r = texture(uScene, uv - c * amt).r;
  col.g = texture(uScene, uv).g;
  col.b = texture(uScene, uv + c * amt).b;

  col += texture(uBloom, uv).rgb * uBloomStrength;
  col = mix(col, uFlashCol, uFlash);
  col = aces(col * 1.05);
  col *= 1.0 - uVignette * r2 * 1.15;

  // Fine grain keeps the gradients from banding on cheap panels.
  float g = fract(sin(dot(uv * uRes + uTime, vec2(12.9898, 78.233))) * 43758.5453);
  col += (g - 0.5) * 0.018;

  fragColor = vec4(col, 1.0);
}`;
