$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\CodexUsageWidget\CodexUsageWidget.csproj'
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'

Write-Host 'Publishing Codex Usage Widget...'

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $output

Write-Host ''
Write-Host "Done: $output\CodexUsageWidget.exe"
