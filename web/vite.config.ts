import { fileURLToPath, URL } from "node:url";

import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vitest/config";

// The browser talks to one origin for the API. Under /api it reaches the gateway, through this proxy in
// development and through nginx in the container, so there is no preflight on every request and the page's
// Content Security Policy names only itself and Keycloak.
const gateway = process.env.MATCHBOOK_GATEWAY ?? "http://localhost:5300";

export default defineConfig({
    plugins: [vue()],
    resolve: {
        alias: {
            "@": fileURLToPath(new URL("./src", import.meta.url)),
        },
    },
    server: {
        // The port the Keycloak client allows as a redirect, so development signs in exactly like the container.
        port: 5301,
        strictPort: true,
        proxy: {
            "/api": { target: gateway, changeOrigin: true },
        },
    },
    preview: {
        port: 5301,
        strictPort: true,
        proxy: {
            "/api": { target: gateway, changeOrigin: true },
        },
    },
    test: {
        environment: "jsdom",
        globals: true,
        include: ["src/**/*.test.ts"],
        coverage: {
            provider: "v8",
            reporter: ["text", "lcov"],
            include: ["src/**/*.{ts,vue}"],
            exclude: ["src/shared/api/generated/**", "src/main.ts", "src/**/*.test.ts"],
        },
    },
    build: {
        // The manifest is what the budget script reads to work out what a first visit costs.
        manifest: true,
        chunkSizeWarningLimit: 170,
    },
});
