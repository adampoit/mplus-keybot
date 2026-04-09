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

## Publishing

The existing publish script builds a self-contained Linux binary:

```powershell
./deploy/build.ps1
```

This writes deployment artifacts to `artifacts/`.

## Systemd deployment

The repository includes example deployment assets under `deploy/`:

- `deploy/systemd/mplus-keybot.service` for running the bot under `systemd`
- `deploy/deploy.sh` as a generic SCP-based deployment script driven by environment variables

The example service reads environment variables from `/etc/mplus-keybot.env`, so a minimal production config can look like:

```text
Discord__Token=discord-bot-token
Discord__Channel=mythic-plus
```

Example:

```bash
DEPLOY_HOST=example.com ./deploy/deploy.sh
```

Optional environment variables:

```text
DEPLOY_USER=root
DEPLOY_PATH=/srv/mplus-keybot
SYSTEMD_PATH=/etc/systemd/system
```

## Open-source notes

- `appsettings.json` is intentionally not committed
- The local SQLite database is intentionally ignored
- Files in `deploy/` are examples and should be adapted to your target host
