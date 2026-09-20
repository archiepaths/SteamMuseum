import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";

const https = {
  cert: readFileSync(new URL("../../secrets/localhost.pem", import.meta.url)),
  key: readFileSync(new URL("../../secrets/localhost.key", import.meta.url))
};

export default defineConfig({
  plugins: [react()],
  server: { host: "localhost", port: 5173, strictPort: true, https },
  preview: { host: "localhost", port: 5173, strictPort: true, https },
});
