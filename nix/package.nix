{
  lib,
  buildDotnetModule,
  buildNpmPackage,
  dotnetCorePackages,
  nodejs,
  version,
  basePath ? "/",
}: let
  api = buildDotnetModule {
    pname = "mplus-keybot";
    inherit version;

    src = lib.fileset.toSource {
      root = ../.;
      fileset = lib.fileset.unions [
        ../Directory.Build.props
        ../Directory.Packages.props
        ../src/MPlusKeybot.Api
      ];
    };

    projectFile = "src/MPlusKeybot.Api/MPlusKeybot.Api.csproj";
    nugetDeps = ./nuget-deps.json;
    executables = ["mplus-keybot"];

    dotnet-sdk = dotnetCorePackages.sdk_10_0;
    dotnet-runtime = dotnetCorePackages.aspnetcore_10_0;

    meta = {
      description = "Discord bot and ASP.NET Core API for Mythic+ updates from Raider.IO";
      homepage = "https://github.com/adampoit/mplus-keybot";
      mainProgram = "mplus-keybot";
      platforms = lib.platforms.unix;
    };
  };

  # React Router 7 SSR app. react-router-serve loads the server bundle from
  # <webRoot>/build/server/index.js and serves <webRoot>/build/client as static
  # assets, resolving both relative to its cwd, so the wrapper cds into the
  # shipped lib directory. Runtime deps (react, react-router, @react-router/serve
  # and its transitive deps) are externalized by the server bundle, so production
  # node_modules must be shipped alongside build/.
  web = buildNpmPackage {
    pname = "mplus-keybot-web";
    inherit version;

    src = lib.fileset.toSource {
      root = ../.;
      fileset = lib.fileset.unions [
        ../tsconfig.json
        ../src/MPlusKeybot.Web/package.json
        ../src/MPlusKeybot.Web/package-lock.json
        ../src/MPlusKeybot.Web/base-path.config.ts
        ../src/MPlusKeybot.Web/vite.config.ts
        ../src/MPlusKeybot.Web/react-router.config.ts
        ../src/MPlusKeybot.Web/tsconfig.json
        ../src/MPlusKeybot.Web/app
      ];
    };

    sourceRoot = "source/src/MPlusKeybot.Web";

    npmDepsHash = "sha256-Joaaz/appLND7yEOHKjiIWd9+bdWW1bEFR2EwSAwxZw=";

    # BASE_PATH is read at build time by react-router.config.ts/vite.config.ts
    # and baked into the server manifest (basename/publicPath) and the client
    # bundle (via import.meta.env.BASE_URL), so the SSR HTML emits prefix-aware
    # asset URLs and route matching honours the sub-path. Defaults to `/` so the
    # published package is path-agnostic; deployments under a sub-path pass
    # basePath explicitly.
    env.BASE_PATH = basePath;

    dontNpmInstall = true;

    installPhase = ''
      runHook preInstall

      webRoot="$out/lib/mplus-keybot-web"
      mkdir -p "$webRoot" "$out/bin"

      cp -r build "$webRoot/build"
      cp -r node_modules "$webRoot/node_modules"
      cp package.json "$webRoot/package.json"

      # Self-contained launcher: cd into the shipped root so express.static
      # resolves build/client (and public/) relative to it, then run
      # react-router-serve with the absolute server build path.
      cat > "$out/bin/mplus-keybot-web" <<EOF
      #!/bin/sh
      cd "$webRoot"
      exec ${lib.getExe nodejs} "$webRoot/node_modules/@react-router/serve/bin.js" "$webRoot/build/server/index.js" "\$@"
      EOF
      chmod +x "$out/bin/mplus-keybot-web"

      runHook postInstall
    '';

    meta = {
      description = "React Router SSR web frontend for mplus-keybot";
      mainProgram = "mplus-keybot-web";
      platforms = nodejs.meta.platforms;
    };
  };
in
  api // {inherit web;}
