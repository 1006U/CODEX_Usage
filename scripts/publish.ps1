$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\CodexUsageWidget\CodexUsageWidget.csproj'
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'
$exe = Join-Path $output 'CodexUsageWidget.exe'
$iconBase64 = Join-Path $PSScriptRoot '..\src\CodexUsageWidget\Resources\CodexUsageMonitor.ico.b64'
$generatedIcon = Join-Path $PSScriptRoot '..\src\CodexUsageWidget\Resources\CodexUsageMonitor.ico'
$publishedIconDir = Join-Path $output 'Resources'
$publishedIcon = Join-Path $publishedIconDir 'CodexUsageMonitor.ico'

Write-Host 'Publishing Codex Usage Widget...'

# Rebuild the .ico from the repository-safe Base64 source before every publish.
# This icon is generated from the image supplied for Codex Usage Monitor.
if (-not (Test-Path $iconBase64)) {
  throw "Icon source was not found: $iconBase64"
}

$iconText = (Get-Content -Raw -Path $iconBase64) -replace '\s', ''
try {
  $iconBytes = [Convert]::FromBase64String($iconText)
  [IO.File]::WriteAllBytes($generatedIcon, $iconBytes)
} catch {
  throw "Could not generate CodexUsageMonitor.ico from Base64: $($_.Exception.Message)"
}

# A running single-file executable locks the publish destination on Windows.
# Stop any existing widget instance before publishing so users do not need to
# manually exit the tray application every time they update it.
$running = Get-Process -Name 'CodexUsageWidget' -ErrorAction SilentlyContinue
if ($running) {
  Write-Host 'Stopping running Codex Usage Widget...'
  $running | Stop-Process -Force

  $deadline = (Get-Date).AddSeconds(5)
  do {
    Start-Sleep -Milliseconds 150
    $stillRunning = Get-Process -Name 'CodexUsageWidget' -ErrorAction SilentlyContinue
  } while ($stillRunning -and (Get-Date) -lt $deadline)

  if ($stillRunning) {
    throw 'Could not stop CodexUsageWidget.exe. Close it manually and run publish again.'
  }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

# Remove an old published executable after the process is stopped. This catches
# stale/broken output and ensures the existence check below refers to this run.
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

# Keep a separate copy for NotifyIcon so the same supplied artwork is used in
# the hidden-icons tray as well as the EXE/shortcut icon.
New-Item -ItemType Directory -Force -Path $publishedIconDir | Out-Null
Copy-Item -Path $generatedIcon -Destination $publishedIcon -Force

Write-Host 'Creating shortcuts...'

$wsh = New-Object -ComObject WScript.Shell

$desktop = [Environment]::GetFolderPath('Desktop')
$desktopShortcut = $wsh.CreateShortcut((Join-Path $desktop 'Codex Usage Monitor.lnk'))
$desktopShortcut.TargetPath = $exe
$desktopShortcut.WorkingDirectory = $output
$desktopShortcut.IconLocation = "$exe,0"
$desktopShortcut.Description = 'Codex 5-hour and weekly usage monitor'
$desktopShortcut.Save()

$startMenu = [Environment]::GetFolderPath('Programs')
$startMenuShortcut = $wsh.CreateShortcut((Join-Path $startMenu 'Codex Usage Monitor.lnk'))
$startMenuShortcut.TargetPath = $exe
$startMenuShortcut.WorkingDirectory = $output
$startMenuShortcut.IconLocation = "$exe,0"
$startMenuShortcut.Description = 'Codex 5-hour and weekly usage monitor'
$startMenuShortcut.Save()

Write-Host ''
Write-Host "Done: $exe"
Write-Host "Icon: $publishedIcon"
Write-Host "Desktop shortcut: $desktop\Codex Usage Monitor.lnk"
Write-Host "Start Menu shortcut: $startMenu\Codex Usage Monitor.lnk"
