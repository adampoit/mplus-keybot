# mplus-keybot

A Discord bot that tracks Mythic+ dungeon runs for your World of Warcraft guild or friend group. Never miss a key completion again—get automatic notifications when followed characters finish dungeons.

<p align="center">
  <img src="screenshot.png" alt="Discord notification showing a completed +9 Magisters' Terrace run with roster details and dungeon image" width="500">
</p>

## What It Does

**For guilds and friend groups:**

- Use `/follow` to open a private Battle.net-authenticated character picker
- Follow or unfollow only characters returned by your Battle.net WoW profile
- Get automatic Discord notifications when followed characters complete dungeons

## Features

- **Slash command registration** (`/follow`) for Battle.net-verified character management
- **Automatic polling** of Raider.IO every 5 minutes for runs
- **SQLite storage** for followed characters and run history—no external database needed
- **React Router management UI** served by ASP.NET Core, backed by JSON API endpoints
- **Lightweight**—runs anywhere .NET runs

## Quick Start

1. **Create a Discord bot** at [discord.com/developers](https://discord.com/developers) and invite it to your server
2. **Create a Battle.net application** and configure the redirect URI to match `Web:PublicBaseUrl` plus `/auth/blizzard/callback`
3. **Copy the example config** and add your Discord and Battle.net credentials:
   ```bash
   cp appsettings.example.json appsettings.json
   # Edit appsettings.json with your Discord token and channel name
   ```
4. **Run it**:
   ```bash
   dotnet run
   ```

The bot will create `mplus-data.db` in the working directory.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Node.js/npm for building the React web UI
- Discord bot token (create one at [Discord Developer Portal](https://discord.com/developers/applications))
- Discord channel where the bot can post messages
- Battle.net application credentials with the `wow.profile` scope
- Public HTTPS URL for the follow management web UI

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
  },
  "Web": {
    "PublicBaseUrl": "https://example.com/mplus-keybot",
    "PathBase": "/mplus-keybot"
  },
  "Blizzard": {
    "ClientId": "battle-net-client-id",
    "ClientSecret": "battle-net-client-secret",
    "Region": "us"
  }
}
```

`Web:PublicBaseUrl` is used for Discord link buttons, Battle.net redirect URIs, and form redirects. If your reverse proxy forwards `/mplus-keybot` to the app, set `Web:PathBase` to `/mplus-keybot`; if it strips the prefix, leave `Web:PathBase` empty while keeping the public URL prefixed.

> ⚠️ `appsettings.json` is git-ignored to prevent accidentally committing secrets.

### Option 2: Environment Variables

```bash
export Discord__Token=your-bot-token-here
export Discord__Channel=mythic-plus
export Web__PublicBaseUrl=https://localhost:5142/mplus-keybot
export Web__PathBase=/mplus-keybot
export Blizzard__ClientId=battle-net-client-id
export Blizzard__ClientSecret=battle-net-client-secret
export Blizzard__Region=us
dotnet run
```

### Option 3: Command Line

```bash
dotnet run --Discord:Token=your-token --Discord:Channel=mythic-plus
```

## Local Battle.net testing without Discord

In development, you can omit `Discord:Token` and use `https://localhost:5142/mplus-keybot/dev/follow?discordUserId=dev-user` to create the same short-lived follow flow and complete the real Battle.net sign-in. Configure your Battle.net app with `https://localhost:5142/mplus-keybot/auth/blizzard/callback` as the redirect URI. The Discord/dev follow link is one-time use and expires quickly; the resulting management session lasts 24 hours.

## Local web development

Install frontend dependencies once:

```bash
npm install
```

Then run the ASP.NET Core app. `Vite.AspNetCore` starts and proxies the Vite dev server automatically, so this single command gives you TypeScript/CSS hot module reload through the ASP.NET Core site:

```bash
dotnet watch --project src/mplus-keybot/mplus-keybot.csproj
```

You can still run `npm run dev` manually when you want to debug Vite directly.

`dotnet build`, `dotnet test`, and `dotnet publish` run `npm run build` automatically when `package.json` is available. Set `SkipNpmBuild=true` if you need to bypass that target.

ASP.NET Core serves the generated Vite bundle from `src/mplus-keybot/web/dist` and uses `Vite.AspNetCore` to resolve hashed production assets from the Vite manifest.

## Testing

```bash
dotnet test
```

The Playwright character-management e2e tests run when Chromium is installed and are skipped otherwise:

```bash
dotnet build tests/mplus-keybot.Tests/mplus-keybot.Tests.csproj
pwsh tests/mplus-keybot.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test --filter CharacterManagementE2ETests
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
Web__PublicBaseUrl=https://example.com/mplus-keybot
Web__PathBase=/mplus-keybot
Blizzard__ClientId=battle-net-client-id
Blizzard__ClientSecret=battle-net-client-secret
Blizzard__Region=us
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
