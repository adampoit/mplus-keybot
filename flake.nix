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
    version = "0.0.0-${self.shortRev or "dirty"}";
  in
    flake-utils.lib.eachDefaultSystem (
      system: let
        pkgs = import nixpkgs {inherit system;};
        package = pkgs.callPackage ./nix/package.nix {
          inherit version;
        };
        fetchDeps = package."fetch-deps";
      in {
        packages = {
          default = package;
          "fetch-deps" = fetchDeps;
        };

        apps = {
          default = {
            type = "app";
            program = "${package}/bin/mplus-keybot";
            meta.description = "Run the mplus-keybot bot";
          };
          "fetch-deps" = {
            type = "app";
            program = "${fetchDeps}";
            meta.description = "Regenerate nix/nuget-deps.json for mplus-keybot";
          };
        };

        checks.default = package;

        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnetCorePackages.sdk_10_0
            alejandra
          ];

          env = {
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
          };
        };

        formatter = pkgs.alejandra;
      }
    )
    // {
      nixosModules.default = import ./nix/module.nix {inherit self;};
    };
}
