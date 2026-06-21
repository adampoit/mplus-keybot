export type BasePathConfig = {
  pathBase: string;
  viteBase: string;
  routerBasename: string;
  developmentRouterBasename: string;
};

export function normalizeBasePath(value?: string): BasePathConfig {
  const trimmed = (value ?? "/").trim();
  const path = trimmed.replace(/^\/+|\/+$/g, "");
  const pathBase = path.length === 0 ? "/" : `/${path}`;
  const viteBase = pathBase === "/" ? "/" : `${pathBase}/`;

  return {
    pathBase,
    viteBase,
    routerBasename: pathBase,
    developmentRouterBasename: viteBase,
  };
}
