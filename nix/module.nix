{self}: {
  config,
  lib,
  pkgs,
  ...
}: let
  cfg = config.services.mplus-keybot;
  webCfg = cfg.web;
in {
  options.services.mplus-keybot = {
    enable = lib.mkEnableOption "the mplus-keybot Discord bot";

    package = lib.mkOption {
      type = lib.types.package;
      default = self.packages.${pkgs.system}.mplus-keybot;
      description = "The mplus-keybot API/bot package to run.";
    };

    dataDir = lib.mkOption {
      type = lib.types.str;
      default = "/var/lib/mplus-keybot";
      description = "Directory used for the SQLite database and working files.";
    };

    environment = lib.mkOption {
      type = lib.types.attrsOf lib.types.str;
      default = {};
      description = ''
        Environment variables shared by the API service. Use `Web__PathBase`
        and `Web__PublicBaseUrl` here to keep the bot's embed/cookie URLs aligned
        with the public origin; the API no longer serves the frontend HTML.
      '';
    };

    environmentFile = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/run/secrets/mplus-keybot.env";
      description = "Optional environment file containing secrets like Discord__Token.";
    };

    web = {
      enable = lib.mkOption {
        type = lib.types.bool;
        default = cfg.enable;
        defaultText = lib.literalExpression "config.services.mplus-keybot.enable";
        description = "Whether to run the React Router SSR web frontend.";
      };

      package = lib.mkOption {
        type = lib.types.nullOr lib.types.package;
        default = null;
        description = ''
          The mplus-keybot-web package to run. Defaults to null, in which case
          the module builds the web package from the flake source with
          `basePath` (the sub-path is baked into the React Router manifest at
          build time, so it cannot be changed at runtime). Set this to override
          with a prebuilt package; when set, `basePath` is ignored.
        '';
      };

      basePath = lib.mkOption {
        type = lib.types.str;
        default = "/";
        example = "/mplus-keybot";
        description = ''
          Sub-path the app is served under (e.g. when behind a reverse proxy
          under a path prefix). This is a build-time value: the module rebuilds
          the web package with it so the SSR manifest and client bundle emit
          prefix-aware URLs. Ignored when `package` is set.
        '';
      };

      port = lib.mkOption {
        type = lib.types.port;
        default = 8083;
        description = "Local port the web server listens on (PORT env).";
      };

      apiBaseUrl = lib.mkOption {
        type = lib.types.str;
        default = "http://127.0.0.1:8082";
        description = ''
          Origin the web server proxies `/api/*` to and uses for SSR API calls.
          Defaults to the API on its conventional local port; set this to match
          `services.mplus-keybot.environment.ASPNETCORE_URLS` if you change it.
        '';
      };

      environment = lib.mkOption {
        type = lib.types.attrsOf lib.types.str;
        default = {};
        description = "Additional environment variables for the web service.";
      };
    };
  };

  config = let
    # Shared env contract: both the AppHost (dev) and these systemd services
    # (prod) must agree on these names. See src/MPlusKeybot.AppHost/AppHost.cs.
    apiService = cfg.enable;
    # The sub-path is baked into the React Router manifest at build time, so the
    # web package is rebuilt from the flake source with the configured basePath
    # unless a prebuilt package is supplied.
    version = "0.0.0-g${self.shortRev or "dirty"}";
    webPkg =
      if webCfg.package != null
      then webCfg.package
      else
        (pkgs.callPackage (self + "/nix/package.nix") {
          inherit version;
          basePath = webCfg.basePath;
        }).web;
  in
    lib.mkMerge [
      (lib.mkIf apiService {
        users.groups.mplus-keybot = {};

        users.users.mplus-keybot = {
          isSystemUser = true;
          group = "mplus-keybot";
          home = cfg.dataDir;
          createHome = false;
        };

        systemd.tmpfiles.rules = [
          "d ${cfg.dataDir} 0750 mplus-keybot mplus-keybot -"
        ];

        systemd.services.mplus-keybot = {
          description = "M+ Keybot (API + Discord bot)";
          after = ["network-online.target"];
          wants = ["network-online.target"];
          wantedBy = ["multi-user.target"];
          environment = cfg.environment;
          serviceConfig =
            {
              Type = "notify";
              User = "mplus-keybot";
              Group = "mplus-keybot";
              WorkingDirectory = cfg.dataDir;
              ExecStart = lib.getExe cfg.package;
              Restart = "always";
              RestartSec = 5;
              NoNewPrivileges = true;
              PrivateTmp = true;
              ProtectHome = true;
              ProtectSystem = "strict";
              ReadWritePaths = [cfg.dataDir];
            }
            // lib.optionalAttrs (cfg.environmentFile != null) {
              EnvironmentFile = cfg.environmentFile;
            };
        };
      })

      (lib.mkIf webCfg.enable {
        assertions = [
          {
            assertion = cfg.enable;
            message = "services.mplus-keybot.web requires services.mplus-keybot.enable (it proxies to the API).";
          }
        ];

        systemd.services.mplus-keybot-web = {
          description = "M+ Keybot web frontend (React Router SSR)";
          after = ["mplus-keybot.service" "network-online.target"];
          wants = ["network-online.target"];
          bindsTo = lib.optional cfg.enable "mplus-keybot.service";
          wantedBy = ["multi-user.target"];
          environment =
            {
              # PORT/HOST/NODE_ENV are read by react-router-serve. The sub-path
              # (basename/publicPath) is baked into the shipped server manifest at
              # build time, so it is not a runtime concern here.
              HOST = "127.0.0.1";
              PORT = toString webCfg.port;
              NODE_ENV = "production";
              API_BASE_URL = webCfg.apiBaseUrl;
            }
            // webCfg.environment;
          serviceConfig = {
            Type = "exec";
            # The assertion above guarantees the mplus-keybot user/group exist.
            User = "mplus-keybot";
            Group = "mplus-keybot";
            ExecStart = lib.getExe webPkg;
            Restart = "always";
            RestartSec = 5;
            NoNewPrivileges = true;
            PrivateTmp = true;
            ProtectSystem = "strict";
          };
        };
      })
    ];
}
