param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$TargetFramework = "net10.0-windows10.0.17763.0",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\publish")
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $root "src\SunTrace.App\SunTrace.App.csproj"
$output = [System.IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path $output) {
    Remove-Item -Path $output -Recurse -Force
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

& dotnet publish $project `
    --configuration $Configuration `
    --framework $TargetFramework `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Публикация SunTrace завершилась с ошибкой (код $LASTEXITCODE)."
}

$executable = Join-Path $output "SunTrace.exe"
if (-not (Test-Path $executable)) {
    throw "После публикации не найден SunTrace.exe."
}

Write-Host "Опубликовано: $output"
