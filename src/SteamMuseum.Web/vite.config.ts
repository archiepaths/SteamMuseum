import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";

export default defineConfig(({ command }) => {
  // Builds and unit tests must not require a developer's private certificate files.
  const https = command === "serve" && !process.env.VITEST ? {
    cert: readFileSync(new URL("../../secrets/localhost.pem", import.meta.url)),
    key: readFileSync(new URL("../../secrets/localhost.key", import.meta.url)),
  } : undefined;
  return {
    plugins: [react()],
    server: { host: "localhost", port: 5173, strictPort: true, https },
    preview: { host: "localhost", port: 5173, strictPort: true, https },
  };
});
