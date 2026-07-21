{
  description = "mplus-keybot";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
    nix-dotnet.url = "github:adampoit/nix-dotnet";
  };

  outputs = {
    self,
    flake-utils,
    nix-dotnet,
    nixpkgs,
  }: let
    version = "0.0.0-g${self.shortRev or "dirty"}";
  in
    flake-utils.lib.eachDefaultSystem (
      system: let
        pkgs = import nixpkgs {inherit system;};
        dotnetSdk = nix-dotnet.lib.${system}.mkDotnet {
          globalJsonPath = ./global.json;
          outputHashes = {
            aarch64-darwin = "sha256-jLnxzcoVDhDrjwkFI9pFWw1W8MVm17VnB2tDvSIiv6w=";
            x86_64-linux = "sha256-veCc0Uh8V2yZR/rB+X+aKmZA+8WecrIFoLc3jzNFOQU=";
          };
        };
        package = pkgs.callPackage ./nix/package.nix {
          inherit dotnetSdk version;
        };
        api = package;
        web = package.web;
        fetchDeps = package."fetch-deps";
      in {
        packages = {
          default = api;
          "mplus-keybot" = api;
          "mplus-keybot-web" = web;
          "fetch-deps" = fetchDeps;
        };

        apps = {
          default = {
            type = "app";
            program = "${api}/bin/mplus-keybot";
            meta.description = "Run the mplus-keybot bot";
          };
          "fetch-deps" = {
            type = "app";
            program = "${fetchDeps}";
            meta.description = "Regenerate nix/nuget-deps.json for mplus-keybot";
          };
        };

        checks = {
          inherit api web;
          default = pkgs.linkFarm "mplus-keybot-checks" [
            {
              name = "api";
              path = api;
            }
            {
              name = "web";
              path = web;
            }
          ];
        };

        devShells.default = pkgs.mkShell {
          packages = [
            dotnetSdk
            pkgs.nodejs_24
            pkgs.alejandra
          ];

          env = {
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
          };
        };

        formatter = pkgs.writeShellApplication {
          name = "alejandra-tree";
          runtimeInputs = [pkgs.alejandra];
          text = ''
            if [ "$#" -eq 0 ]; then
              exec alejandra .
            else
              exec alejandra "$@"
            fi
          '';
        };
      }
    )
    // {
      nixosModules.default = import ./nix/module.nix {inherit self;};
    };
}
