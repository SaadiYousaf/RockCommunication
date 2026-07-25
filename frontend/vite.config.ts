import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// A per-build salt appended to every emitted asset filename. CI passes the commit
// SHA (VITE_BUILD_ID); locally it falls back to "dev". This GUARANTEES each deploy
// produces brand-new asset URLs even if Vite's content hash happens to repeat
// across builds — without it, Cloudflare's `immutable, max-age=1yr` edge cache can
// pin a stale bundle under a reused filename and never revalidate.
const buildId = (process.env.VITE_BUILD_ID || 'dev').slice(0, 12)

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        entryFileNames: `assets/[name]-[hash].${buildId}.js`,
        chunkFileNames: `assets/[name]-[hash].${buildId}.js`,
        assetFileNames: `assets/[name]-[hash].${buildId}[extname]`,
        // Split the core framework libraries into a long-lived `vendor` chunk so
        // they cache independently of app code and don't re-download on every deploy.
        // (rolldown-vite types this as a function, not the object form.)
        manualChunks(id: string) {
          if (/[\\/]node_modules[\\/](react|react-dom|react-router|react-router-dom|react-redux|@reduxjs[\\/]toolkit)[\\/]/.test(id)) {
            return "vendor"
          }
        },
      },
    },
  },
  preview: {
    // Vite blocks unknown Host headers by default — explicitly allow the
    // Cloudflare-tunnel hostname so requests reaching us as
    // "app.smhachieverslifegroup.com" aren't 403'd.
    allowedHosts: ['app.smhachieverslifegroup.com'],
  },
})
