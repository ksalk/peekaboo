/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

const version = readFileSync(new URL('../VERSION', import.meta.url), 'utf8').trim();

export default defineConfig({
  plugins: [svelte()],
  define: {
    __APP_VERSION__: JSON.stringify(version),
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5080',
        ws: true,
      },
    },
  },
});
