{self}: {
  config,
  lib,
  pkgs,
  ...
}: let
  cfg = config.services.mplus-keybot;
in {
  options.services.mplus-keybot = {
    enable = lib.mkEnableOption "the mplus-keybot Discord bot";

    package = lib.mkOption {
      type = lib.types.package;
      default = self.packages.${pkgs.system}.default;
      description = "The mplus-keybot package to run.";
    };

    dataDir = lib.mkOption {
      type = lib.types.str;
      default = "/var/lib/mplus-keybot";
      description = "Directory used for the SQLite database and working files.";
    };

    environment = lib.mkOption {
      type = lib.types.attrsOf lib.types.str;
      default = {};
      description = "Additional environment variables for the bot service.";
    };

    environmentFile = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/run/secrets/mplus-keybot.env";
      description = "Optional environment file containing secrets like Discord__Token.";
    };
  };

  config = lib.mkIf cfg.enable {
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
      description = "M+ Keybot";
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
  };
}
