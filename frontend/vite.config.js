import { defineConfig } from "vite";
import react             from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      "/api":  { target: "http://localhost:8080", changeOrigin: true },
      "/hubs": { target: "http://localhost:8080", changeOrigin: true, ws: true },
    },
  },
  build: {
    outDir: "dist",
    sourcemap: true,
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks: {
          "vendor-react":   ["react", "react-dom"],
          "vendor-recharts": ["recharts"],
          "vendor-signalr":  ["@microsoft/signalr"],
        },
      },
    },
  },
});
