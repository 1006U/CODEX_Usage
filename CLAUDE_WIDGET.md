# Claude Code Usage Monitor (Windows)

A separate **.NET 8 WPF** monitor alongside the existing Codex monitor.

Reference: [INNO-HI/ClaudeUsageWidget](https://github.com/INNO-HI/ClaudeUsageWidget), particularly its read-only Windows usage-fetch approach. The UI here is the existing native WPF taskbar design, not the reference's Node.js/browser UI.

## Features

- Remaining 5-hour and weekly Claude Code subscription quota, reset countdowns
- Mini taskbar widget: `CLAUDE 82% / 5H 82% · WRU 36%`
- Tooltip: `5시간` and `주간` percentages and reset times
- Click mini widget to open detail panel; **−** hides; **×** exits
- Separate tray icon, auto-start registry entry, and desktop/start-menu shortcuts
- Claude tile appears left of the Codex tile when both are running
- ~5-minute polling with jitter and exponential backoff on failures

Taskbar parenting uses unofficial Windows Explorer internals, not an official Weather widget extension. Taskbar layouts, DPI and Explorer updates can affect placement.

## Requirements

Windows 10/11 x64; **.NET 8 SDK** for building (published EXE is self-contained).

Log into Claude Code with a Claude.ai subscription:

```powershell
claude --version
claude login
```

The reader reads the existing `%USERPROFILE%\.claude\.credentials.json` (or `CLAUDE_CONFIG_DIR\.credentials.json`) and calls `https://api.anthropic.com/api/oauth/usage`. It never stores/prints the token, refreshes OAuth credentials, or sends model messages. API-key-only login may not expose subscription usage. If the token expires, refresh it through Claude Code.

**The OAuth usage endpoint is undocumented and may change.** Requests may return 401/403, 429, or unexpected response formats; the widget displays errors and keeps last known values rather than inventing numbers. Avoid polling too frequently. Never share the credentials file.

## Build and publish

From the `CODEX_Usage` repository root:

```powershell
git pull
dotnet build .\src\ClaudeUsageWidget\ClaudeUsageWidget.csproj
.\scripts\publish_claude.ps1
```

The script stops only the Claude widget process, converts its ICO source, and publishes a single standalone EXE:

```text
artifacts\claude-win-x64\ClaudeUsageWidget.exe
```

It also generates a desktop and Start menu shortcut named **Claude Code Usage Monitor**. The existing Codex EXE is untouched and can run simultaneously.

The Windows version of the reference project uses Node.js. This is a separate original WPF implementation inspired by its OAuth usage response approach.

## Refresh safety

The refresh button uses **read-only usage queries only**. It does not send a model prompt or request generated tokens. Repeated clicks within 30 seconds reuse the latest displayed result instead of sending another usage request. After HTTP 429, retries are delayed using the exponential backoff interval. Other Claude Code sessions can still consume tokens independently; a refresh may reveal recently accounted usage.
