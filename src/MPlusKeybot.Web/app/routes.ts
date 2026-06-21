import { index, route, type RouteConfig } from "@react-router/dev/routes";

export default [
  index("routes/home.tsx"),
  route("follow/characters", "routes/follow-characters.tsx"),
  route("dev", "routes/dev.tsx"),
  route("api/*", "routes/api.ts"),
  route("health", "routes/health.ts"),
  route("*", "routes/not-found.tsx"),
] satisfies RouteConfig;
