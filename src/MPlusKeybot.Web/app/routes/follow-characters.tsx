import { CharacterManagementPage } from "../src/pages/CharacterManagementPage";

export function meta() {
  return [{ title: "Manage Characters · mplus-keybot" }];
}

export default function FollowCharactersRoute() {
  return <CharacterManagementPage />;
}
