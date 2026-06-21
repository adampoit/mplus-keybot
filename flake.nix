{
  description = "mplus-keybot";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = {
    self,
    flake-utils,
    nixpkgs,
  }: let
    version = "0.0.0-g${self.shortRev or "dirty"}";
  in
    flake-utils.lib.eachDefaultSystem (
      system: let
        pkgs = import nixpkgs {inherit system;};
        package = pkgs.callPackage ./nix/package.nix {
          inherit version;
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
          packages = with pkgs; [
            dotnetCorePackages.sdk_10_0
            nodejs_24
            alejandra
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
