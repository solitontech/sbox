param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win10-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "SBoxApp\SBoxApp.csproj"
$distRoot = Join-Path $repoRoot "dist\windows"
$publishDir = Join-Path $distRoot "publish"
$artifactName = "SBoxApp-$Runtime.zip"
$artifactPath = Join-Path $distRoot $artifactName

if (Test-Path $distRoot) {
    Remove-Item $distRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish $project `
    -f net8.0-windows10.0.19041.0 `
    -r $Runtime `
    -c $Configuration `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

if (Test-Path $artifactPath) {
    Remove-Item $artifactPath -Force
}

$items = Get-ChildItem -Path $publishDir
Compress-Archive -Path $items.FullName -DestinationPath $artifactPath

Write-Host "Windows build complete. Artifact: $artifactPath"
