import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { viteSingleFile } from "vite-plugin-singlefile";

export default defineConfig({
  plugins: [react(), tailwindcss(), viteSingleFile()],
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "http://127.0.0.1:5077",
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: "../src/SharpPersistence.Migrator.Cli/Ui",
    emptyOutDir: false,
    cssCodeSplit: false,
    assetsInlineLimit: 100000000,
    sourcemap: false,
    minify: "terser",
    terserOptions: {
      compress: { pure_funcs: ["console.log", "console.debug"], passes: 2 },
    },
  },
});
