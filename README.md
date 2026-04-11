# mplus-keybot

A Discord bot that tracks Mythic+ dungeon runs for your World of Warcraft guild or friend group. Never miss a key completion again—get automatic notifications when followed characters finish dungeons, plus weekly affix rotation announcements.

<p align="center">
  <img src="screenshot.png" alt="Discord notification showing a completed +9 Magisters' Terrace run with roster details and dungeon image" width="500">
</p>

## What It Does

**For guilds and friend groups:**

- Use `/follow <character> <realm> <region>` to track any character's Mythic+ runs
- Get automatic Discord notifications when followed characters complete dungeons
- See weekly affix rotations announced when they change

## Features

- **Slash command registration** (`/follow`) for easy character tracking
- **Automatic polling** of Raider.IO (5 min for runs, 1 hour for affixes)
- **SQLite storage** for followed characters and run history—no external database needed
- **Lightweight**—runs anywhere .NET runs

## Quick Start

1. **Create a Discord bot** at [discord.com/developers](https://discord.com/developers) and invite it to your server
2. **Copy the example config** and add your bot token:
   ```bash
   cp appsettings.example.json appsettings.json
   # Edit appsettings.json with your Discord token and channel name
   ```
3. **Run it**:
   ```bash
   dotnet run
   ```

The bot will create `mplus-data.db` in the working directory.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Discord bot token (create one at [Discord Developer Portal](https://discord.com/developers/applications))
- Discord channel where the bot can post messages

## Configuration

The bot uses standard .NET configuration. You can use `appsettings.json`, environment variables, or command-line arguments.

### Option 1: appsettings.json

Copy the example and edit:

```bash
cp appsettings.example.json appsettings.json
```

```json
{
  "Discord": {
    "Token": "your-bot-token-here",
    "Channel": "mythic-plus"
  }
}
```

> ⚠️ `appsettings.json` is git-ignored to prevent accidentally committing secrets.

### Option 2: Environment Variables

```bash
export Discord__Token=your-bot-token-here
export Discord__Channel=mythic-plus
dotnet run
```

### Option 3: Command Line

```bash
dotnet run --Discord:Token=your-token --Discord:Channel=mythic-plus
```

## Deployment

### Nix/NixOS (Recommended)

The repository includes a Nix flake for reproducible builds and deployment:

```bash
# Enter development shell
nix develop

# Build the bot
nix build

# Run directly
nix run
```

#### NixOS Module

For production deployments on NixOS, use the provided module:

```nix
{
  inputs.mplus-keybot.url = "github:adampoit/mplus-keybot";

  outputs = { nixpkgs, mplus-keybot, ... }: {
    nixosConfigurations.mplus-bot = nixpkgs.lib.nixosSystem {
      system = "x86_64-linux";
      modules = [
        mplus-keybot.nixosModules.default
        {
          services.mplus-keybot = {
            enable = true;
            environmentFile = "/run/secrets/mplus-keybot.env";
          };
        }
      ];
    };
  };
}
```

Create `/run/secrets/mplus-keybot.env` with:

```text
Discord__Token=your-bot-token-here
Discord__Channel=mythic-plus
```

After updating NuGet dependencies, regenerate the lock file:

```bash
nix run .#fetch-deps -- ./nix/nuget-deps.json
```

### Standalone Binary (Linux)

You can also build a self-contained binary:

```bash
dotnet publish -c Release -r linux-x64 \
  --self-contained=true \
  -p:PublishSingleFile=true \
  -p:GenerateRuntimeConfigurationFiles=true \
  -o ./artifacts

# Run on target server
./artifacts/mplus-keybot
```

## License

MIT
