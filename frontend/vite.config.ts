import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  preview: {
    // Vite blocks unknown Host headers by default — explicitly allow the
    // Cloudflare-tunnel hostname so requests reaching us as
    // "app.smhachieverslifegroup.com" aren't 403'd.
    allowedHosts: ['app.smhachieverslifegroup.com'],
  },
})
