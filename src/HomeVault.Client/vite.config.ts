import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');
  return {
    base: '/app/',
    plugins: [react()],
    build: { outDir: '../HomeVault/wwwroot/app', emptyOutDir: true },
    server: {
      proxy: {
        '/api': {
          target: env.API_PROXY_TARGET || 'https://localhost:7031',
          changeOrigin: true,
          // Only the local development proxy accepts the ASP.NET development certificate.
          secure: false,
        },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test-setup.ts'],
      clearMocks: true,
    },
  };
});
