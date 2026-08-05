/**
 * Flattens the Vite build into one self-contained page.
 *
 * Two constraints drive this. A published Artifact runs under a strict CSP that
 * blocks every external request, so CSS and JS have to be inlined rather than
 * linked. And the host supplies the <!doctype>/<html>/<head>/<body> skeleton
 * itself, so what we emit is a *fragment* — style, markup, script — not a
 * document.
 *
 *   node scripts/bundle-artifact.mjs [out.html]
 */
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { argv } from 'node:process';

const OUT = argv[2] ?? 'dist/blob-brawl.html';
const html = readFileSync('dist/index.html', 'utf8');

const pick = (re, what) => {
  const m = html.match(re);
  if (!m) throw new Error(`Could not find ${what} in dist/index.html`);
  return m[1];
};

const css = pick(/<style>([\s\S]*?)<\/style>/, '<style> block');
const title = pick(/<title>([\s\S]*?)<\/title>/, '<title>');
const bodyRaw = pick(/<body>([\s\S]*?)<\/body>/, '<body>');

// Vite hoists the module script into <head>, so search the whole document for
// it rather than the body slice, and strip it from the body if it lives there.
const scriptTag = html.match(/<script[^>]*\ssrc="([^"]+)"[^>]*><\/script>/);
if (!scriptTag) throw new Error('Could not find the module <script src> tag');
const body = bodyRaw.replace(scriptTag[0], '').trim();

const asset = scriptTag[1].replace(/^\.?\//, '');
const js = readFileSync(`dist/${asset}`, 'utf8');

// A literal "</script" anywhere in the bundle would close the tag early.
const safeJs = js.replace(/<\/script/gi, '<\\/script');

const out = `<title>${title}</title>
<style>
${css.trim()}
</style>

${body}

<script type="module">
${safeJs}
</script>
`;

writeFileSync(OUT, out);

const kb = (n) => `${(n / 1024).toFixed(1)} kB`;
console.log(`${OUT}  ${kb(out.length)}`);
console.log(`  css ${kb(css.length)}  markup ${kb(body.length)}  js ${kb(js.length)}`);
if (readdirSync('dist/assets').length > 1) {
  console.warn('  warning: more than one asset in dist/assets — only the entry chunk was inlined');
}
