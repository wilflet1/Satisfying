import { defineConfig } from 'vite';

export default defineConfig({
  // Relative base so the build runs from a file:// path, itch.io zip, or any
  // sub-directory without reconfiguration.
  base: './',
  build: {
    target: 'es2020',
    assetsInlineLimit: 100_000,
    rollupOptions: {
      output: {
        // One JS chunk keeps the single-file bundling step trivial.
        manualChunks: undefined,
      },
    },
  },
  server: { host: true, port: 5173 },
});
