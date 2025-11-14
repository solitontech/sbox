param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win10-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "SBoxApp\SBoxApp.csproj"
$distRoot = Join-Path $repoRoot "dist\windows"
$publishDir = Join-Path $distRoot "publish"
$artifactName = "SolitonSBOX-$Runtime.zip"
$artifactPath = Join-Path $distRoot $artifactName
$pfxPath = Join-Path $repoRoot "certificates\SolitonSBOX.pfx"
$pfxPassword = "SolitonTemp123!"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet SDK not found. Install .NET 8 SDK from https://dotnet.microsoft.com/download and rerun the script."
    exit 1
}

if (-not (Get-Command "msbuild.exe" -ErrorAction SilentlyContinue)) {
    Write-Warning "MSBuild not detected. Make sure Visual Studio with MAUI workloads is installed."
}

if (-not (Test-Path "C:\Program Files\Microsoft Visual Studio")) {
    Write-Warning "Visual Studio installation not detected. Ensure VS 2022 with .NET MAUI workloads is installed."
}

if (Test-Path $distRoot) {
    Remove-Item $distRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish $project `
    -f net8.0-windows10.0.19041.0 `
    -p:TargetFramework=net8.0-windows10.0.19041.0 `
    -p:TargetFrameworks=net8.0-windows10.0.19041.0 `
    -r $Runtime `
    -c $Configuration `
    -p:SelfContained=true `
    -p:EnableWindowsTargeting=true `
    -o $publishDir

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $artifactPath

Write-Host "Windows publish output zipped at $artifactPath"

$appPackages = Join-Path (Join-Path $repoRoot "SBoxApp") "bin\$Configuration\net8.0-windows10.0.19041.0\win10-x64\AppPackages"
if (Test-Path $appPackages) {
    $msix = Get-ChildItem -Path $appPackages -Filter *.msix -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $cert = Get-ChildItem -Path $appPackages -Filter *.cer -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($msix) {
        $msixDest = Join-Path $distRoot $msix.Name
        Copy-Item $msix.FullName $msixDest -Force
        Write-Host "MSIX copied to $msixDest"
    }
    else {
        Write-Warning "MSIX file not found under $appPackages"
    }
}
else {
    Write-Warning "AppPackages directory not found ($appPackages)"
}

if (-not $cert) {
    $fallbackCert = Join-Path $repoRoot "SBoxApp\certificates\SolitonSBOX.cer"
    if (Test-Path $fallbackCert) {
        $cert = Get-Item $fallbackCert
    }
}

if ($cert) {
    $certDest = Join-Path $distRoot $cert.Name
    Copy-Item $cert.FullName $certDest -Force
    Write-Host "Certificate copied to $certDest"
    Write-Host "Install the certificate into 'Local Machine -> Trusted People' and 'Local Machine -> Trusted Root Certification Authorities' before installing the MSIX."
}
else {
    Write-Warning "No certificate (.cer) file found in AppPackages."
}

Write-Host "Windows artifacts generated."
