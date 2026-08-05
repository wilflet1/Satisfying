import { CFG } from './config.ts';
import { BLUR_FRAG, COMPOSITE_FRAG, MAX_BLOBS, SCENE_FRAG, VERT } from './shaders/index.ts';

export interface RenderFrame {
  time: number;
  /** World-space camera centre. */
  camX: number;
  camY: number;
  /** World units across the screen width. */
  viewW: number;
  /** Arena boundary radius, and how urgent it is (0..1). */
  ring: number;
  ringWarn: number;
  blobCount: number;
  /** vec4 per blob: x, y, radius, tint. */
  blobs: Float32Array;
  /** vec4 per blob: stretch, axis cos, axis sin, phase. */
  blobDef: Float32Array;
  /** Smooth-union radius, recomputed per frame from the largest blob. */
  smoothK: number;
  /** Aim guide: origin xy, unit direction zw. Zero direction hides it. */
  aim: [number, number, number, number];
  /** Spawn shield: centre xy, radius z, strength w. */
  shield: [number, number, number, number];
  pulse: number;
  pulseX: number;
  pulseY: number;
  chroma: number;
  flash: number;
  flashCol: [number, number, number];
  shakeX: number;
  shakeY: number;
}

interface Target {
  fbo: WebGLFramebuffer;
  tex: WebGLTexture;
  w: number;
  h: number;
}

function compile(gl: WebGL2RenderingContext, type: number, src: string): WebGLShader {
  const sh = gl.createShader(type)!;
  gl.shaderSource(sh, src);
  gl.compileShader(sh);
  if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
    const log = gl.getShaderInfoLog(sh) ?? 'unknown';
    const numbered = src
      .split('\n')
      .map((l, i) => `${String(i + 1).padStart(3)}| ${l}`)
      .join('\n');
    throw new Error(`Shader compile failed: ${log}\n${numbered}`);
  }
  return sh;
}

function link(gl: WebGL2RenderingContext, fragSrc: string): WebGLProgram {
  const p = gl.createProgram()!;
  gl.attachShader(p, compile(gl, gl.VERTEX_SHADER, VERT));
  gl.attachShader(p, compile(gl, gl.FRAGMENT_SHADER, fragSrc));
  gl.linkProgram(p);
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) {
    throw new Error(`Program link failed: ${gl.getProgramInfoLog(p)}`);
  }
  return p;
}

/** Caches uniform locations so the hot path never calls getUniformLocation. */
class Program {
  private loc = new Map<string, WebGLUniformLocation | null>();
  constructor(
    readonly gl: WebGL2RenderingContext,
    readonly prog: WebGLProgram,
  ) {}
  u(name: string): WebGLUniformLocation | null {
    if (!this.loc.has(name)) this.loc.set(name, this.gl.getUniformLocation(this.prog, name));
    return this.loc.get(name)!;
  }
  use() {
    this.gl.useProgram(this.prog);
  }
}

export class Renderer {
  readonly gl: WebGL2RenderingContext;
  private scene: Program;
  private blur: Program;
  private composite: Program;
  private texFormat: number;
  private texType: number;

  private sceneTarget!: Target;
  private bloomA!: Target;
  private bloomB!: Target;

  private cssW = 0;
  private cssH = 0;
  private dpr = 1;
  private sceneScale: number = CFG.render.sceneScale;

  /** Rolling frame-time average used to back off resolution on weak devices. */
  private frameAvg = 16;
  /** Seconds before the next resolution change is allowed. */
  private adaptCooldown = 0;

  constructor(readonly canvas: HTMLCanvasElement) {
    const gl = canvas.getContext('webgl2', {
      alpha: false,
      antialias: false,
      depth: false,
      stencil: false,
      powerPreference: 'high-performance',
    });
    if (!gl) throw new Error('WebGL2 is not available on this device.');
    this.gl = gl;

    // Half-float targets keep bloom from clipping; fall back to 8-bit if the
    // device can't render to them.
    const hdr = gl.getExtension('EXT_color_buffer_float') !== null;
    this.texFormat = hdr ? gl.RGBA16F : gl.RGBA8;
    this.texType = hdr ? gl.HALF_FLOAT : gl.UNSIGNED_BYTE;

    this.scene = new Program(gl, link(gl, SCENE_FRAG));
    this.blur = new Program(gl, link(gl, BLUR_FRAG));
    this.composite = new Program(gl, link(gl, COMPOSITE_FRAG));

    this.resize();
  }

  private makeTarget(w: number, h: number): Target {
    const gl = this.gl;
    const tex = gl.createTexture()!;
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, this.texFormat, w, h, 0, gl.RGBA, this.texType, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    const fbo = gl.createFramebuffer()!;
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return { fbo, tex, w, h };
  }

  private disposeTarget(t?: Target) {
    if (!t) return;
    this.gl.deleteFramebuffer(t.fbo);
    this.gl.deleteTexture(t.tex);
  }

  resize() {
    const gl = this.gl;
    // Cap DPR: this is a fill-rate-bound fullscreen shader, and 3x on a phone
    // buys nothing visible while costing more than half the frame budget.
    this.dpr = Math.min(window.devicePixelRatio || 1, 2);
    this.cssW = this.canvas.clientWidth || window.innerWidth;
    this.cssH = this.canvas.clientHeight || window.innerHeight;

    const w = Math.max(1, Math.round(this.cssW * this.dpr));
    const h = Math.max(1, Math.round(this.cssH * this.dpr));
    this.canvas.width = w;
    this.canvas.height = h;

    const sw = Math.max(1, Math.round(w * this.sceneScale));
    const sh = Math.max(1, Math.round(h * this.sceneScale));
    const bw = Math.max(1, Math.round(w * CFG.render.bloomScale));
    const bh = Math.max(1, Math.round(h * CFG.render.bloomScale));

    this.disposeTarget(this.sceneTarget);
    this.disposeTarget(this.bloomA);
    this.disposeTarget(this.bloomB);
    this.sceneTarget = this.makeTarget(sw, sh);
    this.bloomA = this.makeTarget(bw, bh);
    this.bloomB = this.makeTarget(bw, bh);

    gl.viewport(0, 0, w, h);
  }

  /** Aspect ratio (height / width), for deriving the visible world height. */
  get aspect(): number {
    return this.canvas.height / Math.max(1, this.canvas.width);
  }

  /**
   * Reads back the scene buffer for tests. The default framebuffer is blank
   * after compositing unless `preserveDrawingBuffer` is set, which costs real
   * performance on mobile — so tests sample the offscreen scene target, which
   * persists, rather than making every player pay for the check.
   */
  debugSampleScene(step = 6): { lit: number; peak: number; samples: number } {
    const gl = this.gl;
    const { w, h } = this.sceneTarget;
    // The scene target is half-float when the device supports it, and reading a
    // float buffer as bytes is an INVALID_OPERATION — so the read type has to
    // follow the buffer's actual format.
    const float = this.texType !== gl.UNSIGNED_BYTE;
    const buf = float ? new Float32Array(w * h * 4) : new Uint8Array(w * h * 4);
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.sceneTarget.fbo);
    gl.readPixels(0, 0, w, h, gl.RGBA, float ? gl.FLOAT : gl.UNSIGNED_BYTE, buf as never);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);

    // Normalise both paths to 0..1 per channel so the thresholds mean the same
    // thing regardless of which buffer format the device gave us.
    const scale = float ? 1 : 1 / 255;
    let lit = 0;
    let peak = 0;
    let samples = 0;
    for (let y = 0; y < h; y += step) {
      for (let x = 0; x < w; x += step) {
        const i = (y * w + x) * 4;
        const l = (buf[i] + buf[i + 1] + buf[i + 2]) * scale;
        if (l > 0.09) lit++;
        if (l > peak) peak = l;
        samples++;
      }
    }
    return { lit, peak: +peak.toFixed(3), samples };
  }

  private drawQuad() {
    this.gl.drawArrays(this.gl.TRIANGLES, 0, 3);
  }

  private bind(t: Target | null) {
    const gl = this.gl;
    if (t) {
      gl.bindFramebuffer(gl.FRAMEBUFFER, t.fbo);
      gl.viewport(0, 0, t.w, t.h);
    } else {
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    }
  }

  render(f: RenderFrame, frameMs: number) {
    this.adapt(frameMs);
    const gl = this.gl;

    // ---- Scene ---------------------------------------------------------
    this.bind(this.sceneTarget);
    this.scene.use();
    const s = this.scene;
    gl.uniform2f(s.u('uRes'), this.sceneTarget.w, this.sceneTarget.h);
    gl.uniform1f(s.u('uTime'), f.time);
    gl.uniform2f(s.u('uCam'), f.camX, f.camY);
    gl.uniform1f(s.u('uViewW'), f.viewW);
    gl.uniform1f(s.u('uRing'), f.ring);
    gl.uniform1f(s.u('uRingWarn'), f.ringWarn);
    gl.uniform1f(s.u('uBevel'), CFG.render.bevel);
    gl.uniform1f(s.u('uSmoothK'), f.smoothK);
    gl.uniform1f(s.u('uWobble'), CFG.goo.wobble);
    gl.uniform4f(s.u('uAim'), f.aim[0], f.aim[1], f.aim[2], f.aim[3]);
    gl.uniform4f(s.u('uShield'), f.shield[0], f.shield[1], f.shield[2], f.shield[3]);
    gl.uniform1f(s.u('uPulse'), f.pulse);
    gl.uniform2f(s.u('uPulsePos'), f.pulseX, f.pulseY);
    gl.uniform1i(s.u('uBlobCount'), Math.min(f.blobCount, MAX_BLOBS));
    gl.uniform4fv(s.u('uBlobs'), f.blobs);
    gl.uniform4fv(s.u('uBlobDef'), f.blobDef);
    this.drawQuad();

    // ---- Bloom: bright-pass + separable blur ---------------------------
    const b = this.blur;
    b.use();
    gl.uniform1i(b.u('uTex'), 0);
    gl.activeTexture(gl.TEXTURE0);

    this.bind(this.bloomA);
    gl.bindTexture(gl.TEXTURE_2D, this.sceneTarget.tex);
    gl.uniform2f(b.u('uTexel'), 1 / this.bloomA.w, 1 / this.bloomA.h);
    gl.uniform2f(b.u('uDir'), 1, 0);
    gl.uniform1f(b.u('uThreshold'), 0.8);
    this.drawQuad();

    this.bind(this.bloomB);
    gl.bindTexture(gl.TEXTURE_2D, this.bloomA.tex);
    gl.uniform2f(b.u('uTexel'), 1 / this.bloomB.w, 1 / this.bloomB.h);
    gl.uniform2f(b.u('uDir'), 0, 1);
    gl.uniform1f(b.u('uThreshold'), -1);
    this.drawQuad();

    // ---- Composite -----------------------------------------------------
    this.bind(null);
    const c = this.composite;
    c.use();
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.sceneTarget.tex);
    gl.uniform1i(c.u('uScene'), 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.bloomB.tex);
    gl.uniform1i(c.u('uBloom'), 1);
    gl.uniform2f(c.u('uRes'), this.canvas.width, this.canvas.height);
    gl.uniform1f(c.u('uTime'), f.time);
    gl.uniform1f(c.u('uBloomStrength'), CFG.render.bloomStrength);
    gl.uniform1f(c.u('uChroma'), f.chroma);
    gl.uniform1f(c.u('uFlash'), f.flash);
    gl.uniform3f(c.u('uFlashCol'), f.flashCol[0], f.flashCol[1], f.flashCol[2]);
    gl.uniform1f(c.u('uVignette'), 0.55);
    gl.uniform2f(c.u('uShake'), f.shakeX, f.shakeY);
    this.drawQuad();
  }

  /**
   * Drops the scene buffer scale when frames run long. Resolution is the only
   * knob worth touching automatically — cutting effects would change how the
   * game looks between devices, but a softer image reads the same.
   */
  private adapt(frameMs: number) {
    const clamped = Math.min(frameMs, 200);
    // Weight by elapsed time, not frame count: a device running at 5 fps must
    // reach the backoff decision in the same *wall-clock* time as one at 60.
    const w = Math.min(1, (clamped / 1000) * 3);
    this.frameAvg += (clamped - this.frameAvg) * w;

    this.adaptCooldown -= clamped / 1000;
    if (this.adaptCooldown > 0) return;

    if (this.frameAvg > 22 && this.sceneScale > CFG.render.minSceneScale) {
      this.sceneScale = Math.max(CFG.render.minSceneScale, this.sceneScale - 0.15);
      this.resize();
      this.adaptCooldown = 1.5;
    } else if (this.frameAvg < 13 && this.sceneScale < CFG.render.sceneScale) {
      this.sceneScale = Math.min(CFG.render.sceneScale, this.sceneScale + 0.1);
      this.resize();
      this.adaptCooldown = 4;
    }
  }
}
