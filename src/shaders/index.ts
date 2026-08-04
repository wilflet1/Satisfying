export const MAX_BLOBS = 40;
export const MAX_PRIMS = 28;

/** Fullscreen triangle generated from gl_VertexID — no vertex buffers needed. */
export const VERT = `#version 300 es
out vec2 vUv;
void main(){
  vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
  vUv = p;
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

/**
 * The scene pass. Everything — tunnel, obstacles, and the player's liquid metal —
 * is one screen-space signed-distance field. Blobs union with a polynomial
 * smooth-min, which is why splitting and merging read as a fluid stretching and
 * coalescing rather than as sprites appearing and disappearing.
 */
export const SCENE_FRAG = `#version 300 es
precision highp float;

in vec2 vUv;
out vec4 fragColor;

uniform vec2  uRes;
uniform float uTime;
uniform float uCamY;
uniform float uViewH;
uniform float uChanHalf;
uniform float uBevel;
uniform float uSmoothK;
uniform float uSpeedN;     // 0..1 normalised channel speed
uniform float uPulse;      // merge shockwave, 1 -> 0
uniform vec2  uPulsePos;
uniform int   uBlobCount;
uniform int   uPrimCount;
uniform float uWobble;     // domain-warp amplitude, world units
uniform vec4  uBlobs[${MAX_BLOBS}];      // xy = centre, z = radius, w = tint (0 chrome, 1 gold)
uniform vec4  uBlobDef[${MAX_BLOBS}];    // x = stretch, yz = stretch axis (cos,sin), w = phase
uniform vec4  uPrims[${MAX_PRIMS}];      // xy = centre, zw = half-extents (saw: z = radius, w = rotation)
uniform vec2  uPrimMeta[${MAX_PRIMS}];   // x = kind (0 wall, 1 door, 2 saw), y = flash

const float PI = 3.14159265;

vec2 worldFromUv(vec2 uv){
  return vec2((uv.x - 0.5) * uChanHalf * 2.0, uCamY + (1.0 - uv.y) * uViewH);
}

float hash21(vec2 p){
  p = fract(p * vec2(123.34, 456.21));
  p += dot(p, p + 45.32);
  return fract(p.x * p.y);
}
float vnoise(vec2 p){
  vec2 i = floor(p), f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  float a = hash21(i), b = hash21(i + vec2(1.0, 0.0));
  float c = hash21(i + vec2(0.0, 1.0)), d = hash21(i + vec2(1.0, 1.0));
  return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float fbm(vec2 p){ return vnoise(p) * 0.62 + vnoise(p * 2.31) * 0.28; }

/** IQ cosine palette — the oil-slick iridescence. */
vec3 pal(float t){
  return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.0, 0.33, 0.67)));
}

float smin(float a, float b, float k){
  float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
  return mix(b, a, h) - k * h * (1.0 - h);
}

float sdRoundBox(vec2 p, vec2 b, float r){
  vec2 q = abs(p) - b + r;
  return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

/** Toothed disc. Not a metric SDF, but the gradient is good enough to shade. */
float sdSaw(vec2 p, float r, float rot){
  float a = atan(p.y, p.x) + rot;
  return length(p) - (r + cos(a * 8.0) * r * 0.20);
}

/**
 * Low-frequency domain warp. Displacing the sample point before measuring
 * distance is what turns perfect circles into something with surface tension:
 * silhouettes ripple, and because the warp is evaluated per sample the finite
 * -difference normals inherit it too, so the *shading* crawls as well as the
 * outline. Sine-based rather than value noise — this runs three times per
 * shaded pixel, and four sines beat eight hashes.
 */
vec2 gooWarp(vec2 p){
  float t = uTime * 0.7;
  return vec2(
    sin(p.y * 0.21 + t) + 0.5 * sin(p.y * 0.37 - t * 1.3 + p.x * 0.11),
    sin(p.x * 0.19 - t * 1.1) + 0.5 * sin(p.x * 0.33 + t * 0.9 + p.y * 0.13)
  ) * uWobble;
}

float blobDist(vec2 p){
  vec2 w = p + gooWarp(p);
  float d = 1e9;
  for (int i = 0; i < ${MAX_BLOBS}; i++){
    if (i >= uBlobCount) break;
    vec4 B = uBlobs[i];
    vec4 D = uBlobDef[i];

    // Rotate into the stretch frame, then scale x by (1+k) and y by 1/(1+k).
    // Area is exactly preserved, so squash and stretch never changes how much
    // metal the player appears to have.
    vec2 q = w - B.xy;
    vec2 e = vec2(D.y * q.x + D.z * q.y, -D.z * q.x + D.y * q.y);
    float k = 1.0 + D.x;
    e.x /= k;
    e.y *= k;

    d = smin(d, length(e) - B.z, uSmoothK);
  }
  return d;
}

/**
 * Influence-weighted tint (so an absorbed droplet bleeds gold into the chrome)
 * plus the radius of the nearest blob. The radius matters: the dome shading
 * below bevels the edge over a fixed depth, and a depth wider than the blob
 * itself means the surface is *all* bevel and never curves back to face the
 * viewer — which renders a 6-way split as flat pastel discs, not beads of metal.
 */
vec2 blobTintRadius(vec2 p){
  float wsum = 1e-5, tsum = 0.0;
  float best = 1e9, rNear = 4.0;
  for (int i = 0; i < ${MAX_BLOBS}; i++){
    if (i >= uBlobCount) break;
    vec4 B = uBlobs[i];
    float di = length(p - B.xy) - B.z;
    float w = exp(-max(di, 0.0) * 0.22);
    wsum += w;
    tsum += w * B.w;
    if (di < best){ best = di; rNear = B.z; }
  }
  return vec2(tsum / wsum, rNear);
}

float primDist(vec2 p){
  float d = 1e9;
  for (int i = 0; i < ${MAX_PRIMS}; i++){
    if (i >= uPrimCount) break;
    vec4 P = uPrims[i];
    float di = uPrimMeta[i].x > 1.5
      ? sdSaw(p - P.xy, P.z, P.w)
      : sdRoundBox(p - P.xy, P.zw, min(1.2, min(P.z, P.w) * 0.6));
    d = min(d, di);
  }
  return d;
}

/** Nearest primitive's distance plus its kind and flash, for shading. */
vec3 primNearest(vec2 p){
  float d = 1e9, kind = 0.0, flash = 0.0;
  for (int i = 0; i < ${MAX_PRIMS}; i++){
    if (i >= uPrimCount) break;
    vec4 P = uPrims[i];
    float k = uPrimMeta[i].x;
    float di = k > 1.5
      ? sdSaw(p - P.xy, P.z, P.w)
      : sdRoundBox(p - P.xy, P.zw, min(1.2, min(P.z, P.w) * 0.6));
    if (di < d){ d = di; kind = k; flash = uPrimMeta[i].y; }
  }
  return vec3(d, kind, flash);
}

/**
 * Procedural studio environment sampled by the reflection vector. The shape that
 * sells chrome is contrast, not colour: a dark floor, a bright sky, and a hot
 * band where they meet. A smooth gradient renders as pearl instead.
 */
vec3 envMap(vec3 r){
  float y = r.y;
  vec3 ground = vec3(0.010, 0.014, 0.032);
  vec3 sky = mix(vec3(0.06, 0.13, 0.32), vec3(0.55, 0.78, 1.15), smoothstep(0.0, 0.85, y));
  vec3 c = mix(ground, sky, smoothstep(-0.20, 0.28, y));
  // Hot horizon band.
  c += vec3(1.00, 0.97, 0.94) * smoothstep(0.24, 0.0, abs(y - 0.10)) * 0.95;
  // Tight key light — the specular glint that reads as polished.
  c += vec3(1.00, 0.95, 0.88) * pow(max(dot(r, normalize(vec3(0.40, 0.75, 0.52))), 0.0), 40.0) * 4.0;
  return c;
}

/**
 * detail = 0 for the copy sampled through the metal's meniscus. That lookup is
 * already smeared by refraction, so the extra noise octave and the speed streaks
 * are invisible there and not worth doubling the per-pixel cost for.
 */
vec3 background(vec2 p, float detail){
  vec3 col = vec3(0.012, 0.015, 0.026);

  // Drifting nebula haze.
  float n = detail > 0.5
    ? fbm(vec2(p.x * 0.013, p.y * 0.013 - uTime * 0.012))
    : vnoise(vec2(p.x * 0.013, p.y * 0.013 - uTime * 0.012)) * 0.62;
  col += vec3(0.05, 0.10, 0.22) * n * n * 1.6;

  // Channel walls: hard falloff outside, emissive lip at the boundary.
  float ex = abs(p.x) - uChanHalf;
  float lip = exp(-abs(ex) * 0.55);
  col += vec3(0.08, 0.26, 0.62) * lip * 0.34;
  col *= mix(1.0, 0.12, smoothstep(0.0, 5.0, ex));

  // Depth rungs, brightest near the walls so the centre stays readable. Faint
  // by design — this is a tunnel, not ruled paper.
  float rung = abs(fract(p.y / 34.0) - 0.5) * 2.0;
  float line = smoothstep(0.975, 1.0, rung);
  col += vec3(0.03, 0.08, 0.17) * line * (0.10 + 0.9 * pow(abs(p.x) / uChanHalf, 3.0));

  // Speed streaks that only show up once the channel is moving fast.
  if (detail > 0.5 && uSpeedN > 0.01){
    float streak = smoothstep(0.75, 1.0, vnoise(vec2(p.x * 0.55, p.y * 0.03 + uTime * 3.0)));
    col += vec3(0.18, 0.30, 0.55) * streak * uSpeedN * 0.28;
  }

  return col;
}

void main(){
  vec2 uv = vUv;
  vec2 p = worldFromUv(uv);
  vec3 col = background(p, 1.0);

  // ---- Obstacles -------------------------------------------------------
  vec3 pn = primNearest(p);
  float dp = pn.x;
  vec3 danger = pn.y < 0.5  ? vec3(0.16, 0.88, 1.00)   // slit gate  — cyan
              : pn.y < 1.5  ? vec3(1.00, 0.62, 0.16)   // blast door — amber
                            : vec3(1.00, 0.22, 0.30);  // saw blade  — red
  float flash = pn.z;

  if (uPrimCount > 0){
    // Halo bleeds outward from the silhouette.
    col += danger * exp(-max(dp, 0.0) * 0.42) * 0.30 * (1.0 + flash * 2.5);

    float apa = fwidth(dp) * 1.2 + 1e-4;
    float pm = smoothstep(apa, -apa, dp);
    if (pm > 0.001){
      vec3 body = vec3(0.030, 0.036, 0.052);
      body += danger * exp(-abs(dp) * 0.85) * (0.85 + flash * 3.5);
      body += danger * 0.05 * (0.5 + 0.5 * sin(p.y * 2.6 + p.x * 0.6 + uTime * 2.2));
      col = mix(col, body, pm);
    }
  }

  // ---- Liquid metal ----------------------------------------------------
  float d = blobDist(p);
  float aa = fwidth(d) * 1.2 + 1e-4;
  float mask = smoothstep(aa, -aa, d);

  if (mask > 0.001){
    // Gradient is only worth paying for on pixels the metal actually touches.
    // Forward differences, not central: half the field evaluations, and the
    // half-texel bias it introduces is invisible against a 3-unit bevel.
    float e = 0.35;
    vec2 gRaw = vec2(
      blobDist(p + vec2(e, 0.0)) - d,
      blobDist(p + vec2(0.0, e)) - d
    ) / e;
    float glen = max(length(gRaw), 1e-3);
    vec2 g = gRaw / glen;

    vec2 tr = blobTintRadius(p);
    float tint = tr.x;
    // Bevel is a fraction of the blob's own radius, so a droplet and a merged
    // mass both curve across their full width.
    float bevel = max(0.6, tr.y * uBevel);

    // Map the interior distance onto a dome so the 2D field shades as 3D metal.
    // The domain warp, the anisotropic squash and a wide smooth-union all break
    // the metric, so the raw field value is no longer a distance — it grows far
    // faster than one unit per unit. Dividing by the gradient length restores a
    // distance-like quantity. Without it the dome saturates a pixel inside the
    // edge and every blob shades as a flat white disc.
    float t = clamp(-d / (glen * bevel), 0.0, 1.0);
    float z = sqrt(max(0.0, 1.0 - (1.0 - t) * (1.0 - t)));
    vec3 N = normalize(vec3(-g * (1.0 - z * 0.90), max(z, 0.03)));

    vec3 R = reflect(vec3(0.0, 0.0, -1.0), N);
    vec3 metal = envMap(R) * mix(vec3(0.86, 0.92, 1.05), vec3(1.40, 0.92, 0.32), tint);

    // Iridescence hugs the rim: a high power keeps it an oil-slick edge instead
    // of a pastel wash across the whole surface.
    float fres = pow(1.0 - clamp(N.z, 0.0, 1.0), 5.0);
    metal += pal(fres * 0.8 + p.y * 0.004 + uTime * 0.05) * fres * 0.85;

    // Refract the tunnel through the meniscus.
    vec2 roff = N.xy * 0.05 * (1.0 - z);
    metal = mix(background(worldFromUv(uv + roff), 0.0) * 1.35, metal, 0.70 + 0.30 * fres);

    // Depth-driven internal light. Thick parts of the mass glow from within,
    // thin necks and the edges of droplets stay translucent — the read that
    // separates a body of liquid from a painted circle.
    metal += vec3(0.30, 0.56, 1.00) * smoothstep(0.35, 1.0, z) * 0.30;
    metal += vec3(1.00, 0.86, 0.62) * pow(smoothstep(0.55, 1.0, z), 3.0) * 0.10;
    // Wet highlight, offset from the crown so it slides as the surface moves.
    metal += vec3(1.0) * pow(smoothstep(0.90, 1.0, z), 2.0) * 0.30;

    col = mix(col, metal, mask);
  }

  // Contact glow: metal lights the tunnel around it.
  col += vec3(0.22, 0.42, 0.85) * exp(-max(d, 0.0) * 0.30) * 0.16;

  // ---- Merge shockwave -------------------------------------------------
  // Radius is expressed in channel widths, not absolute world units, so the
  // ring stays a ring instead of swallowing the screen when the channel is
  // narrow — and it is thin and brief, because it fires once per fusion.
  if (uPulse > 0.0){
    float rd = length(p - uPulsePos);
    float ring = exp(-abs(rd - (1.0 - uPulse) * uChanHalf * 1.15) * 0.30);
    col += vec3(0.55, 0.80, 1.0) * ring * uPulse * uPulse * 0.75;
  }

  fragColor = vec4(col, 1.0);
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
