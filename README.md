# Codex Usage Monitor

Windows desktop widget for monitoring the remaining Codex subscription usage without opening the ChatGPT/Codex usage screen.

The design is inspired by the workflow of Claude usage widgets: keep a tiny always-on-top monitor visible, poll only usage/rate-limit information, and never send model prompts just to check quota.

## Features

- Remaining **5-hour** Codex usage
- Remaining **weekly (7-day)** Codex usage
- Live reset countdown for both windows
- Dark glass-style always-on-top widget
- Borderless drag-to-move window
- Manual refresh
- Automatic sync around once per minute
- ±10% refresh jitter so multiple clients do not poll at exactly the same instant
- Exponential retry backoff on failures (1 → 2 → 4 → 8 → 16 minutes)
- Keeps the last successful values visible if a later refresh fails
- Uses the locally installed **official Codex CLI app-server**
- No Codex model prompt is sent to obtain quota information
- Does **not** directly parse/store/copy `~/.codex/auth.json`

## Why the implementation differs from Claude usage widgets

A Claude widget can reuse Claude Code's local OAuth credential and call Anthropic's usage endpoint directly. For this Codex monitor we deliberately keep authentication inside the official Codex CLI instead.

The monitor starts:

```text
codex app-server
```

and uses its local JSON-RPC interface:

```text
initialize
initialized
account/rateLimits/read
```

The usage response contains rate-limit windows. The monitor identifies:

- `300` minutes → 5-hour window
- `10080` minutes → 7-day window

and displays:

```text
remaining = 100 - usedPercent
```

This gives the same practical experience as the Claude widget pattern—usage only, no model call—while avoiding a second implementation of Codex login/token handling.

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

The repository includes:

```powershell
.\scripts\publish.ps1
```

or run manually:

```powershell
dotnet publish .\src\CodexUsageWidget\CodexUsageWidget.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

## Security

The application intentionally leaves Codex authentication to the installed Codex CLI. It does not log access tokens and does not need a separate OpenAI login screen.

The widget only asks the local Codex app-server for account rate-limit information. The Codex app-server itself performs the authenticated backend request using the account already signed into Codex.

## Current UI

The main card shows two headline values:

```text
CODEX Usage Monitor

5시간                 주간
53%                   61%
RESET 22:10           RESET 10/15 14:20

5시간 한도                     53% 남음
███████████░░░░░░░░
2시간 14분 후 초기화

주간 한도                      61% 남음
████████████░░░░░░░
5일 18시간 후 초기화

정상 · 1분 간격 자동 동기화      19:42:10
```

## Next planned improvements

- System tray icon with 5-hour percentage
- Start with Windows option
- Save/restore widget position
- Configurable low-usage notifications
- Optional 7-day usage history/sparkline
- GitHub Actions release build for portable EXE
