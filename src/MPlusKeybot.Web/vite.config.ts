import { dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { reactRouter } from "@react-router/dev/vite";
import { defineConfig, type Plugin } from "vite";
import { normalizeBasePath } from "./base-path.config";

const projectRoot = dirname(fileURLToPath(import.meta.url));
const basePath = normalizeBasePath(process.env.BASE_PATH);

export default defineConfig({
  base: basePath.viteBase,
  plugins: [canonicalBasePathRedirect(basePath.pathBase), reactRouter()],
  root: projectRoot,
});

function canonicalBasePathRedirect(pathBase: string): Plugin {
  return {
    name: "mplus-keybot-canonical-base-path-redirect",
    configureServer(server) {
      if (pathBase === "/") return;

      server.middlewares.use((request, response, next) => {
        const url = new URL(request.url ?? "/", "http://localhost");
        if (url.pathname !== pathBase) {
          next();
          return;
        }

        response.statusCode = 308;
        response.setHeader("Location", `${pathBase}/${url.search}`);
        response.end();
      });
    },
  };
}
