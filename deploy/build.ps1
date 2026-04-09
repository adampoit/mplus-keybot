#!/usr/bin/env pwsh
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

dotnet publish (Join-Path $repoRoot "mplus-keybot.csproj") -c Release -r linux-x64 --self-contained=true -p:PublishSingleFile=true -p:GenerateRuntimeConfigurationFiles=true -o (Join-Path $repoRoot "artifacts")
