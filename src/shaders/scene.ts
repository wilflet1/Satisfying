export const MAX_BLOBS = 64;

/**
 * Arena scene pass. Every body in the game — players, chunks in flight, free
 * pellets, splash — lives in one signed-distance field and unions with a
 * polynomial smooth-min, so mass moving between them reads as liquid flowing
 * rather than as sprites being swapped.
 */
export const SCENE_FRAG = `#version 300 es
precision highp float;

in vec2 vUv;
out vec4 fragColor;

uniform vec2  uRes;
uniform float uTime;
uniform vec2  uCam;        // world-space camera centre
uniform float uViewW;      // world units across the screen width
uniform float uRing;       // arena boundary radius
uniform float uRingWarn;   // 0..1, rises as the ring closes
uniform float uBevel;
uniform float uSmoothK;
uniform float uWobble;
uniform float uPulse;
uniform vec2  uPulsePos;
uniform vec4  uAim;        // xy = origin, zw = direction (zero = hidden)
uniform vec4  uShield;     // xyz = centre + radius, w = strength (0 = hidden)
uniform int   uBlobCount;
uniform vec4  uBlobs[${MAX_BLOBS}];    // xy = centre, z = radius, w = hue index (-1 = neutral)
uniform vec4  uBlobDef[${MAX_BLOBS}];  // x = stretch, yz = axis (cos,sin), w = unused

/**
 * Screen to world. vUv.y is 0 at the *bottom* of the framebuffer and 1 at the
 * top, and world y grows upward, so the two agree and y must not be flipped
 * here. Negating it renders the whole arena upside down, which shows up as
 * "movement is inverted" — the blob really does move up, the view just draws
 * it going down.
 */
vec2 worldFromUv(vec2 uv){
  float viewH = uViewW * uRes.y / uRes.x;
  return uCam + vec2((uv.x - 0.5) * uViewW, (uv.y - 0.5) * viewH);
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

vec3 pal(float t){
  return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.0, 0.33, 0.67)));
}

/** Per-player tint. Neutral goo is pale gold so loose mass always reads as loot. */
vec3 hueColor(float h){
  if (h < -0.5) return vec3(1.30, 1.05, 0.55);
  return 0.66 + 0.72 * cos(6.28318 * (h / 8.0 + vec3(0.0, 0.33, 0.67)));
}

float smin(float a, float b, float k){
  float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
  return mix(b, a, h) - k * h * (1.0 - h);
}

/**
 * Low-frequency domain warp — the living surface. Evaluated per sample so the
 * finite-difference normals inherit it and the shading crawls with the outline,
 * not just the silhouette. Sines rather than value noise: this runs three times
 * per shaded pixel and four sines beat eight hashes.
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
    // Rotate into the stretch frame, scale x by (1+k) and y by 1/(1+k): area is
    // exactly preserved, so squash never appears to change a player's mass.
    vec2 q = w - B.xy;
    vec2 e = vec2(D.y * q.x + D.z * q.y, -D.z * q.x + D.y * q.y);
    float k = 1.0 + D.x;
    e.x /= k;
    e.y *= k;
    d = smin(d, length(e) - B.z, uSmoothK);
  }
  return d;
}

/** Influence-weighted hue plus the nearest blob's radius, for shading. */
vec2 blobHueRadius(vec2 p){
  float wsum = 1e-5, hsum = 0.0;
  float best = 1e9, rNear = 4.0;
  for (int i = 0; i < ${MAX_BLOBS}; i++){
    if (i >= uBlobCount) break;
    vec4 B = uBlobs[i];
    float di = length(p - B.xy) - B.z;
    float w = exp(-max(di, 0.0) * 0.30);
    wsum += w;
    hsum += w * B.w;
    if (di < best){ best = di; rNear = B.z; }
  }
  return vec2(hsum / wsum, rNear);
}

/**
 * Procedural studio environment. What sells chrome is contrast, not colour: a
 * dark floor, a bright sky, and a hot band where they meet.
 */
vec3 envMap(vec3 r){
  float y = r.y;
  vec3 ground = vec3(0.010, 0.014, 0.032);
  vec3 sky = mix(vec3(0.06, 0.13, 0.32), vec3(0.55, 0.78, 1.15), smoothstep(0.0, 0.85, y));
  vec3 c = mix(ground, sky, smoothstep(-0.20, 0.28, y));
  c += vec3(1.00, 0.97, 0.94) * smoothstep(0.24, 0.0, abs(y - 0.10)) * 0.95;
  c += vec3(1.00, 0.95, 0.88) * pow(max(dot(r, normalize(vec3(0.40, 0.75, 0.52))), 0.0), 40.0) * 4.0;
  return c;
}

vec3 background(vec2 p, float detail){
  vec3 col = vec3(0.012, 0.015, 0.026);
  float rad = length(p);

  // Floor haze, so the empty middle of the arena isn't dead black.
  if (detail > 0.5){
    float n = vnoise(vec2(p.x * 0.013, p.y * 0.013 - uTime * 0.012));
    col += vec3(0.05, 0.10, 0.22) * n * n * 1.5;
  }

  // Floor grid — the only thing giving the arena a sense of scale and motion.
  vec2 g = abs(fract(p / 34.0) - 0.5) * 2.0;
  float line = max(smoothstep(0.975, 1.0, g.x), smoothstep(0.975, 1.0, g.y));
  col += vec3(0.04, 0.09, 0.19) * line * smoothstep(uRing * 1.05, uRing * 0.2, rad);

  // The boundary itself: a hot rim that reddens and pulses as it closes in.
  float dr = abs(rad - uRing);
  vec3 ringCol = mix(vec3(0.16, 0.72, 1.00), vec3(1.00, 0.28, 0.24), uRingWarn);
  float pulse = 0.75 + 0.25 * sin(uTime * (2.0 + uRingWarn * 5.0));
  col += ringCol * exp(-dr * 0.16) * 0.42 * pulse;
  col += ringCol * smoothstep(2.4, 0.0, dr) * 0.9 * pulse;

  // Outside is lethal and looks it.
  float out_ = smoothstep(0.0, 26.0, rad - uRing);
  col = mix(col, vec3(0.10, 0.012, 0.02), out_ * 0.85);

  return col;
}

void main(){
  vec2 uv = vUv;
  vec2 p = worldFromUv(uv);
  vec3 col = background(p, 1.0);

  float d = blobDist(p);
  float aa = fwidth(d) * 1.2 + 1e-4;
  float mask = smoothstep(aa, -aa, d);

  if (mask > 0.001){
    // Forward differences: half the field evaluations of a central stencil, and
    // the half-texel bias is invisible against a radius-scaled bevel.
    float e = 0.35;
    vec2 gRaw = vec2(
      blobDist(p + vec2(e, 0.0)) - d,
      blobDist(p + vec2(0.0, e)) - d
    ) / e;
    float glen = max(length(gRaw), 1e-3);
    vec2 g = gRaw / glen;

    vec2 hr = blobHueRadius(p);
    float hue = hr.x;
    // Bevel is a fraction of the blob's own radius so a pellet and a bloated
    // player both curve across their full width.
    float bevel = max(0.6, hr.y * uBevel);

    // The warp, the anisotropic squash and a wide smooth-union all break the
    // metric, so the field value is no longer a distance — it grows far faster
    // than one unit per unit. Dividing by gradient length restores a
    // distance-like quantity; without it every blob shades as a flat white disc.
    float t = clamp(-d / (glen * bevel), 0.0, 1.0);
    float z = sqrt(max(0.0, 1.0 - (1.0 - t) * (1.0 - t)));
    vec3 N = normalize(vec3(-g * (1.0 - z * 0.90), max(z, 0.03)));

    vec3 R = reflect(vec3(0.0, 0.0, -1.0), N);
    vec3 metal = envMap(R) * hueColor(hue);

    // Iridescence hugs the rim rather than washing the whole surface.
    float fres = pow(1.0 - clamp(N.z, 0.0, 1.0), 5.0);
    metal += pal(fres * 0.8 + hue * 0.12 + uTime * 0.05) * fres * 0.8;

    vec2 roff = N.xy * 0.05 * (1.0 - z);
    metal = mix(background(worldFromUv(uv + roff), 0.0) * 1.35, metal, 0.70 + 0.30 * fres);

    // Depth-driven internal light: thick parts glow, thin necks stay
    // translucent — the read that separates a body of liquid from a flat disc.
    metal += hueColor(hue) * 0.34 * smoothstep(0.35, 1.0, z);
    metal += vec3(1.0) * pow(smoothstep(0.90, 1.0, z), 2.0) * 0.30;

    col = mix(col, metal, mask);
  }

  // ---- Aim trajectory ---------------------------------------------------
  // A shooter you cannot aim is not a game. This draws where the shot will go:
  // a dotted line, because a solid one reads as a laser that already fired.
  if (dot(uAim.zw, uAim.zw) > 0.0001){
    // Start the guide just outside the blob, so the dashes read as a
    // trajectory leaving you rather than a line drawn across you.
    vec2 q = p - (uAim.xy + uAim.zw * (uShield.z + 2.0));
    float along = dot(q, uAim.zw);
    float side = abs(q.x * uAim.w - q.y * uAim.z);
    float len = 78.0;
    if (along > 0.0 && along < len){
      float dash = step(0.40, fract(along * 0.10 - uTime * 1.6));
      float fade = 1.0 - along / len;
      col += vec3(0.90, 0.97, 1.0) * dash * fade * smoothstep(2.4, 0.0, side) * 1.5;
    }
    // Arrowhead, so the direction is unambiguous at a glance.
    vec2 tip = uAim.xy + uAim.zw * len;
    col += vec3(0.9, 0.97, 1.0) * smoothstep(4.2, 0.0, length(p - tip)) * 1.2;
  }

  // ---- Spawn shield -----------------------------------------------------
  // Invulnerability that isn't visible is indistinguishable from luck.
  if (uShield.w > 0.0){
    float sd = abs(length(p - uShield.xy) - uShield.z);
    float ring = smoothstep(2.6, 0.0, sd);
    float shimmer = 0.6 + 0.4 * sin(uTime * 7.0 - length(p - uShield.xy) * 0.4);
    col += vec3(0.55, 0.9, 1.0) * ring * shimmer * uShield.w * 0.9;
  }

  // Contact glow, so every body lights the floor around it.
  col += vec3(0.22, 0.42, 0.85) * exp(-max(d, 0.0) * 0.30) * 0.13;

  if (uPulse > 0.0){
    float rd = length(p - uPulsePos);
    float ring = exp(-abs(rd - (1.0 - uPulse) * 46.0) * 0.30);
    col += vec3(0.55, 0.80, 1.0) * ring * uPulse * uPulse * 0.8;
  }

  fragColor = vec4(col, 1.0);
}`;
