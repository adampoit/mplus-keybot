{
  lib,
  buildDotnetModule,
  buildNpmPackage,
  dotnetCorePackages,
  version,
}: let
  frontend = buildNpmPackage {
    pname = "mplus-keybot-frontend";
    inherit version;

    src = lib.fileset.toSource {
      root = ../.;
      fileset = lib.fileset.unions [
        ../package.json
        ../package-lock.json
        ../vite.config.ts
        ../src/mplus-keybot/web
      ];
    };

    npmDepsHash = "sha256-y909GTRGqhGbBrI0BLIk4TCk/CmKZApfXH9257uHMHI=";

    installPhase = ''
      runHook preInstall

      mkdir -p $out
      cp -r src/mplus-keybot/web/dist $out/dist

      runHook postInstall
    '';
  };
in
  buildDotnetModule {
    pname = "mplus-keybot";
    inherit version;

    src = lib.fileset.toSource {
      root = ../.;
      fileset = ../src/mplus-keybot;
    };

    projectFile = "src/mplus-keybot/mplus-keybot.csproj";
    nugetDeps = ./nuget-deps.json;
    executables = ["mplus-keybot"];

    dotnet-sdk = dotnetCorePackages.sdk_10_0;
    dotnet-runtime = dotnetCorePackages.aspnetcore_10_0;

    postInstall = ''
      cp -r ${frontend}/dist $out/lib/mplus-keybot/web/dist
    '';

    meta = {
      description = "Discord bot that posts Mythic+ updates from Raider.IO";
      homepage = "https://github.com/adampoit/mplus-keybot";
      mainProgram = "mplus-keybot";
      platforms = lib.platforms.unix;
    };
  }
