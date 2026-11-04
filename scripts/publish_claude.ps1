$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\ClaudeUsageWidget\ClaudeUsageWidget.csproj'
$output = Join-Path $PSScriptRoot '..\artifacts\claude-win-x64'
$exe = Join-Path $output 'ClaudeUsageWidget.exe'
$iconBase64 = Join-Path $PSScriptRoot '..\src\ClaudeUsageWidget\Resources\ClaudeUsageMonitor.ico.b64'
$generatedIcon = Join-Path $PSScriptRoot '..\src\ClaudeUsageWidget\Resources\ClaudeUsageMonitor.ico'
$publishedIconDir = Join-Path $output 'Resources'
$publishedIcon = Join-Path $publishedIconDir 'ClaudeUsageMonitor.ico'

Write-Host 'Publishing Claude Code Usage Widget...'

# Rebuild the .ico from the repository-safe Base64 source before every publish.
# Original terracotta Claude-monitor artwork, with multiple sizes for EXE,
# desktop shortcuts and the Windows notification area.
if (-not (Test-Path $iconBase64)) {
  throw "Icon source was not found: $iconBase64"
}

$iconText = (Get-Content -Raw -Path $iconBase64) -replace '\s', ''
try {
  $iconBytes = [Convert]::FromBase64String($iconText)
  if ($iconBytes.Length -lt 256 -or $iconBytes[0] -ne 0 -or $iconBytes[1] -ne 0 -or $iconBytes[2] -ne 1 -or $iconBytes[3] -ne 0) {
    throw 'Decoded ICO has an invalid header.'
  }
  [IO.File]::WriteAllBytes($generatedIcon, $iconBytes)
} catch {
  throw "Could not generate ClaudeUsageMonitor.ico from Base64: $($_.Exception.Message)"
}

# A running single-file executable locks the publish destination on Windows.
$running = Get-Process -Name 'ClaudeUsageWidget' -ErrorAction SilentlyContinue
if ($running) {
  Write-Host 'Stopping running Claude Code Usage Widget...'
  $running | Stop-Process -Force

  $deadline = (Get-Date).AddSeconds(5)
  do {
    Start-Sleep -Milliseconds 150
    $stillRunning = Get-Process -Name 'ClaudeUsageWidget' -ErrorAction SilentlyContinue
  } while ($stillRunning -and (Get-Date) -lt $deadline)

  if ($stillRunning) {
    throw 'Could not stop ClaudeUsageWidget.exe. Close it manually and run publish again.'
  }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

if (Test-Path $exe) {
  Remove-Item $exe -Force
}

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  "-p:ApplicationIcon=$generatedIcon" `
  -o $output

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE. Shortcuts were not changed."
}

if (-not (Test-Path $exe)) {
  throw "Publish completed but executable was not found: $exe"
}

# Keep a standalone ICO next to the published EXE. NotifyIcon and shortcuts use
# this file directly, instead of relying on the shell to extract an icon from the
# single-file EXE (which is more prone to stale icon-cache entries).
New-Item -ItemType Directory -Force -Path $publishedIconDir | Out-Null
Copy-Item -Path $generatedIcon -Destination $publishedIcon -Force

Write-Host 'Creating shortcuts...'

$wsh = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath('Desktop')
$startMenu = [Environment]::GetFolderPath('Programs')
$desktopLinkPath = Join-Path $desktop 'Claude Code Usage Monitor.lnk'
$startMenuLinkPath = Join-Path $startMenu 'Claude Code Usage Monitor.lnk'

# Delete the old .lnk files first. Reusing an existing shortcut often leaves the
# old icon cached by Explorer even when IconLocation is changed.
Remove-Item $desktopLinkPath -Force -ErrorAction SilentlyContinue
Remove-Item $startMenuLinkPath -Force -ErrorAction SilentlyContinue

$desktopShortcut = $wsh.CreateShortcut($desktopLinkPath)
$desktopShortcut.TargetPath = $exe
$desktopShortcut.WorkingDirectory = $output
$desktopShortcut.IconLocation = "$publishedIcon,0"
$desktopShortcut.Description = 'Claude Code 5-hour and weekly usage monitor'
$desktopShortcut.Save()

$startMenuShortcut = $wsh.CreateShortcut($startMenuLinkPath)
$startMenuShortcut.TargetPath = $exe
$startMenuShortcut.WorkingDirectory = $output
$startMenuShortcut.IconLocation = "$publishedIcon,0"
$startMenuShortcut.Description = 'Claude Code 5-hour and weekly usage monitor'
$startMenuShortcut.Save()

# Ask Explorer to refresh shortcut/icon presentation. This does not restart
# Explorer or delete user settings; it simply requests a shell icon refresh.
$ie4uinit = Join-Path $env:SystemRoot 'System32\ie4uinit.exe'
if (Test-Path $ie4uinit) {
  try {
    Start-Process -FilePath $ie4uinit -ArgumentList '-show' -WindowStyle Hidden -Wait
  } catch {
    # Non-fatal: the new shortcut/icon files are already correct.
  }
}

Write-Host ''
Write-Host "Done: $exe"
Write-Host "Icon: $publishedIcon"
Write-Host "Desktop shortcut: $desktopLinkPath"
Write-Host "Start Menu shortcut: $startMenuLinkPath"
