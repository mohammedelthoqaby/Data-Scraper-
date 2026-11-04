// @ts-check
import { defineConfig } from 'astro/config';
import react from '@astrojs/react';

// https://astro.build/config
export default defineConfig({
  output: 'static',
  devToolbar: {
    enabled: false,
  },
  integrations: [react()],
  server: {
    port: 3000,
  },
  vite: {
    server: {
      proxy: {
        '/api': {
          target: 'http://localhost:5188',
          changeOrigin: true,
        },
        '/swagger': {
          target: 'http://localhost:5188',
          changeOrigin: true,
        }
      }
    }
  }
});
