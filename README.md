# mplus-keybot

`mplus-keybot` is a .NET Discord bot that watches Raider.IO for followed characters, posts newly completed Mythic+ runs, and announces weekly affix changes.

## What it does

- Registers a `/follow` slash command in a Discord server
- Polls Raider.IO every 5 minutes for new Mythic+ runs
- Polls Raider.IO every hour for weekly affix changes
- Stores followed characters and announced runs in a local SQLite database

## Requirements

- .NET 10 SDK
- A Discord bot token
- A Discord channel name for announcements

## Configuration

The app reads configuration from standard .NET configuration sources, including `appsettings.json` and environment variables.

Example configuration is provided in `appsettings.example.json`:

```json
{
  "Discord": {
    "Token": "discord-bot-token",
    "Channel": "mythic-plus"
  }
}
```

Equivalent environment variables:

```text
Discord__Token=discord-bot-token
Discord__Channel=mythic-plus
```

`appsettings.json` is ignored by git so local secrets do not end up in the repository.

## Running locally

```bash
dotnet run
```

The bot creates `mplus-data.db` in the working directory.

## Nix

The repository now includes a flake that can build the bot, open a development shell, and expose a reusable NixOS module.

```bash
nix develop
nix build
nix run
```

If NuGet dependencies change, regenerate `nix/nuget-deps.json` with:

```bash
nix run .#fetch-deps -- ./nix/nuget-deps.json
```

### NixOS module

The flake exposes `nixosModules.default`, so another flake can consume it like this:

```nix
{
  inputs.mplus-keybot.url = "github:adampoit/mplus-keybot";

  outputs = { nixpkgs, mplus-keybot, ... }: {
    nixosConfigurations.mplus-bot = nixpkgs.lib.nixosSystem {
      system = "x86_64-linux";
      modules = [
        mplus-keybot.nixosModules.default
        ({ ... }: {
          services.mplus-keybot = {
            enable = true;
            environmentFile = "/run/secrets/mplus-keybot.env";
          };
        })
      ];
    };
  };
}
```

The environment file should contain the Discord settings:

```text
Discord__Token=discord-bot-token
Discord__Channel=mythic-plus
```

## Publishing

If you need a standalone Linux binary outside Nix, publish it directly with `dotnet`:

```bash
dotnet publish -c Release -r linux-x64 --self-contained=true -p:PublishSingleFile=true -p:GenerateRuntimeConfigurationFiles=true -o artifacts
```

This writes deployment artifacts to `artifacts/`.

## License

MIT
