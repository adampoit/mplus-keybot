import { fileURLToPath } from "node:url";
import type { Config } from "@react-router/dev/config";
import { normalizeBasePath } from "./base-path.config";

const basePath = normalizeBasePath(process.env.BASE_PATH);

export default {
  appDirectory: fileURLToPath(new URL("./app", import.meta.url)),
  basename:
    process.env.NODE_ENV === "development"
      ? basePath.developmentRouterBasename
      : basePath.routerBasename,
  ssr: true,
} satisfies Config;
