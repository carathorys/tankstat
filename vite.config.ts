/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  root: 'src/frontend',
  plugins: [react()],
  server: { proxy: { '/graphql': 'http://localhost:5080' } },
  build: { outDir: '../../dist', emptyOutDir: true },
  test: {
    environment: 'jsdom',
    setupFiles: ['../../tests/frontend/setup.ts'],
    include: ['../../tests/frontend/**/*.{unit,integration}.test.{ts,tsx}'],
    coverage: { provider: 'v8', include: ['**'], exclude: ['main.tsx'] },
  },
})
