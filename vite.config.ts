import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'
import { appVersion } from './src/frontend/appVersion.ts'
import { SERVER_PATHS } from './src/frontend/pwa/serverPaths.ts'
import { initSchemeScript, initSchemeStyle } from './src/frontend/theme/initScheme.ts'

// https://vite.dev/config/
export default defineConfig({
  root: 'src/frontend',
  // The web app's version (appVersion.ts), from the build's VERSION like the API's: the footer shows it even while the server is out of reach.
  define: { __APP_VERSION__: JSON.stringify(appVersion(process.env.VERSION)) },
  plugins: [
    react(),
    // The colour scheme the user chose, from the first paint (theme/initScheme.ts): the theme's styles only arrive with the app's script.
    // At the end of <head>, after the theme-color meta it sets.
    {
      name: 'tankstat-init-scheme',
      transformIndexHtml: () => [
        { tag: 'style', children: initSchemeStyle(), injectTo: 'head' },
        { tag: 'script', children: initSchemeScript(), injectTo: 'head' },
      ],
    },
    // Installable web app (docs/install-as-an-app.md): the manifest, and a service worker that keeps the built shell (HTML, scripts,
    // styles, icons) so the app starts fast and opens offline. The API, sign-in, pictures and uploads are never cached. A new build
    // is downloaded in the background and waits; UpdateNotice offers a Reload, and nothing reloads by itself.
    VitePWA({
      registerType: 'prompt', // a new version waits until the person reloads (UpdateNotice); nothing reloads by itself
      injectRegister: false, // main.tsx registers the worker itself (pwa/registerApp.ts)
      manifest: {
        name: 'Tankstat',
        short_name: 'Tankstat',
        description: 'Fuel consumption and expense tracking for your vehicles',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        background_color: '#1f2a6b',
        theme_color: '#1f2a6b',
        icons: [
          { src: '/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: '/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: '/icon-maskable-192.png', sizes: '192x192', type: 'image/png', purpose: 'maskable' },
          { src: '/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
          { src: '/favicon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg,webmanifest}'],
        navigateFallback: 'index.html',
        clientsClaim: true, // the first worker controls the page at once; a later one waits for the person's go-ahead (skipWaiting is off)
        navigateFallbackDenylist: SERVER_PATHS, // the API, the OIDC endpoints, pictures and uploads (tested in ServerPaths.unit.test.ts)
        cleanupOutdatedCaches: true,
      },
    }),
  ],
  // The API's endpoints. Only /auth/oidc/ and /auth/token/ of /auth: the frontend's own modules live in src/frontend/auth/ (served at /auth/...).
  server: {
    proxy: {
      '/graphql': 'http://localhost:5080',
      '/auth/oidc/': 'http://localhost:5080',
      '/auth/token/': 'http://localhost:5080',
      '/media': 'http://localhost:5080',
      '/imports': 'http://localhost:5080',
    },
  },
  build: { outDir: '../../dist', emptyOutDir: true },
  // The tests' settings are in vitest.config.ts, which builds on this file.
})
