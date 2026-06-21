// Driven by Vite's `base` config (set from BASE_PATH at build time) so the
// client stays in sync with the deployed sub-path without a hardcoded value.
// `import.meta.env.BASE_URL` always ends with a trailing slash; normalise to
// the bare prefix (empty string for the root path).
export const basePath = (import.meta.env.BASE_URL || "/").replace(/\/+$/, "");

export function withBasePath(path: string) {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  return basePath ? `${basePath}${normalizedPath}` : normalizedPath;
}
