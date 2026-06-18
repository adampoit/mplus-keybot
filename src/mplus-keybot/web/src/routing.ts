export function routeBase() {
  const root = document.getElementById("root");
  return root?.dataset.routeBase ?? "/";
}
