$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\CodexUsageWidget\CodexUsageWidget.csproj'
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'
$exe = Join-Path $output 'CodexUsageWidget.exe'

Write-Host 'Publishing Codex Usage Widget...'

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $output

if (-not (Test-Path $exe)) {
  throw "Publish completed but executable was not found: $exe"
}

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
Write-Host "Desktop shortcut: $desktop\Codex Usage Monitor.lnk"
Write-Host "Start Menu shortcut: $startMenu\Codex Usage Monitor.lnk"
