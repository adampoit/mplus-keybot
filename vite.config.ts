import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  root: "src/mplus-keybot/web",
  plugins: [react()],
  publicDir: false,
  server: {
    strictPort: true,
  },
  build: {
    manifest: true,
    outDir: "dist",
    rollupOptions: {
      input: {
        app: "src/mplus-keybot/web/src/main.tsx",
      },
    },
  },
});
