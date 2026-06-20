import { DevToolsPage } from "../src/pages/DevToolsPage";

export function meta() {
  return [{ title: "Dev Tools · mplus-keybot" }];
}

export default function DevRoute() {
  return <DevToolsPage />;
}
