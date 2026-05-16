{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  version,
}:
buildDotnetModule {
  pname = "mplus-keybot";
  inherit version;

  src = lib.fileset.toSource {
    root = ../.;
    fileset = lib.fileset.unions [
      ../AchievementRules.cs
      ../Program.cs
      ../RaiderIOClient.cs
      ../mplus-keybot.csproj
      ../database
      ../jobs
    ];
  };

  projectFile = "mplus-keybot.csproj";
  nugetDeps = ./nuget-deps.json;
  executables = ["mplus-keybot"];

  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.runtime_10_0;

  meta = {
    description = "Discord bot that posts Mythic+ updates from Raider.IO";
    homepage = "https://github.com/adampoit/mplus-keybot";
    mainProgram = "mplus-keybot";
    platforms = lib.platforms.unix;
  };
}
