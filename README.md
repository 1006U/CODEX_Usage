# Codex Usage Widget

Windows desktop widget for monitoring the remaining Codex subscription usage.

## Features

- Shows remaining **5-hour** Codex usage
- Shows remaining **weekly (7-day)** Codex usage
- Shows reset time for each window
- Refreshes automatically every 60 seconds
- Manual refresh button
- Borderless, draggable, always-on-top desktop widget
- Uses the locally installed **official Codex CLI app-server**
- Does **not** read or store `~/.codex/auth.json` directly

## Requirements

- Windows 10/11
- .NET 8 SDK (for building)
- OpenAI Codex CLI installed and available on `PATH`
- Signed in to Codex with ChatGPT

Check Codex first:

```powershell
codex --version
codex login status
```

## Build

```powershell
dotnet build .\src\CodexUsageWidget\CodexUsageWidget.csproj
```

Run:

```powershell
dotnet run --project .\src\CodexUsageWidget\CodexUsageWidget.csproj
```

## Publish a standalone EXE

```powershell
dotnet publish .\src\CodexUsageWidget\CodexUsageWidget.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

Output is created under:

```text
src\CodexUsageWidget\bin\Release\net8.0-windows\win-x64\publish\
```

## How usage is read

The widget starts:

```text
codex app-server
```

and talks to it over local JSON-RPC/stdin/stdout. It requests `account/rateLimits/read` and identifies the general limits by window duration:

- `300` minutes -> 5-hour limit
- `10080` minutes -> weekly limit

Remaining percentage is calculated as `100 - usedPercent`.

## Security

The widget intentionally does not parse, copy, log, or upload your Codex authentication tokens. Authentication remains owned by the installed Codex CLI.

## Status

This repository currently contains the first working implementation scaffold. The Codex app-server protocol can evolve, so parsing is deliberately tolerant of additional response fields.
