import { useOutletContext } from "react-router";
import { HomePage } from "../src/pages/HomePage";
import type { AppOutletContext } from "../root";

export function meta() {
  return [{ title: "mplus-keybot" }];
}

export default function HomeRoute() {
  const { session } = useOutletContext<AppOutletContext>();
  return <HomePage session={session} />;
}
