# Distribution Guide

This document covers how to create installable packages for both Windows and macOS, as well as how to manage certificates/signatures.

## Directory Layout

```
scripts/
  publish-windows.ps1   # Windows release helper
  publish-mac.sh        # macOS release helper
SBoxApp/
  certificates/         # Signing cert (PFX + CER) used during build
dist/
  windows/              # generated MSIX + zipped publish + CER copy
  mac/                  # generated .pkg + zipped publish
```

## Windows Packaging

### Prerequisites

1. .NET 8 SDK (`winget install --id Microsoft.DotNet.SDK.8`).
2. Visual Studio 2022 with the `.NET MAUI` workload (ensures WinUI dependencies).
3. Windows SDK (provides `signtool.exe`). Install via:
   ```powershell
   winget install --id Microsoft.WindowsSDK -e
   ```
4. Self-signed certificate. The solution already contains `SBoxApp/certificates/SolitonSBOX.pfx` and `.cer`.

### Signing Certificate

If you need to regenerate the cert:
```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=SolitonSBOX" -FriendlyName "SolitonSBOX Dev Cert" -CertStoreLocation "Cert:\CurrentUser\My"
Export-PfxCertificate -Cert $cert -FilePath .\SBoxApp\certificates\SolitonSBOX.pfx -Password (ConvertTo-SecureString "SolitonTemp123!" -AsPlainText -Force)
Export-Certificate -Cert $cert -FilePath .\SBoxApp\certificates\SolitonSBOX.cer
```
Then import it (with admin rights) so MSIX signing works:
```powershell
$pwd = ConvertTo-SecureString 'SolitonTemp123!' -AsPlainText -Force
Import-PfxCertificate -FilePath .\SBoxApp\certificates\SolitonSBOX.pfx -Password $pwd -CertStoreLocation Cert:\LocalMachine\My
```
Windows also needs the public certificate trusted for install testing:
```powershell
certutil -addstore "TrustedPeople" .\SBoxApp\certificates\SolitonSBOX.cer
certutil -addstore "Root"           .\SBoxApp\certificates\SolitonSBOX.cer
```

### Publish Steps

Run from repo root:
```powershell
scripts\publish-windows.ps1
```
The script:
- Builds the `Release` MAUI WinUI target.
- Automatically signs the MSIX via MSBuild using `SBoxApp/certificates/SolitonSBOX.pfx`.
- Zips the publish folder (`dist/windows/SolitonSBOX-win10-x64.zip`).
- Copies the MSIX and a copy of the `.cer` to `dist/windows/` for distribution.

### Verifying the MSIX
```powershell
Get-AuthenticodeSignature .\dist\windows\SBoxApp_1.0.0.0_x64.msix
```
Status should be `Valid`. If not signed, install the Windows SDK and rerun the script or manually run `signtool` with the provided PFX.

### Installing for Testers
Distribute the MSIX and CER. Testers must import the CER into both `Trusted People` and `Trusted Root Certification Authorities`, then install the MSIX (instructions are in the root README). Remind them to enable Developer Mode on Windows when using self-signed packages.

## macOS Packaging

### Prerequisites

Must run on macOS (or a macOS VM) with:

1. Xcode and Command Line Tools:
   ```bash
   sudo xcode-select --switch /Applications/Xcode.app/Contents/Developer
   sudo xcodebuild -runFirstLaunch
   ```
2. .NET 8 SDK:
   ```bash
   brew install --cask dotnet-sdk@8
   sudo ln -sfn /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
   dotnet --list-sdks
   ```
3. MAUI workloads:
   ```bash
   sudo dotnet workload install maui maui-maccatalyst
   dotnet workload list
   ```
4. If you plan to distribute a signed `.pkg`, you must have Apple Developer ID Application and Developer ID Installer certificates.

The script checks for `dotnet`, Xcode tools, `codesign`, and workloads, and exits with guidance if anything is missing.

### Publish Steps

From repo root:
```bash
chmod +x ./scripts/publish-mac.sh
bash ./scripts/publish-mac.sh
```
The script outputs:
- `dist/mac/SolitonSBOX-maccatalyst-x64.zip` (zipped publish).
- `.pkg` installer copied from `SBoxApp/bin/Release/net8.0-maccatalyst/maccatalyst-x64/`.

### Signing & Notarizing (optional)

If you need a trusted `.pkg`, sign the app bundle and pkg:
```bash
codesign --force --deep --options runtime \
  --entitlements Platforms/MacCatalyst/Entitlements.plist \
  --sign "Developer ID Application: Your Company (TEAMID)" \
  SBoxApp.app

productbuild --component SBoxApp.app /Applications unsigned.pkg
productsign --sign "Developer ID Installer: Your Company (TEAMID)" \
  unsigned.pkg SBoxApp.pkg
```
Optionally notarize (`xcrun notarytool submit ...` then `xcrun stapler staple`).

Without Developer ID certificates, distribute the source so users can build/run locally as described in the README; unsigned packages will not retain entitlements and may crash.

## Configuration Defaults

`SboxConfiguration` defaults to:
```
Bot Gateway: 127.0.0.1:2025
Server:      192.168.1.235:50500
```
Users can change these via the Settings screen; values are persisted per machine.

## Troubleshooting

### MSIX still reports “Publisher certificate could not be verified”

- Ensure the `.cer` is imported into both `Trusted People` and `Trusted Root Certification Authorities`:
  ```powershell
  certutil -addstore "TrustedPeople" .\dist\windows\SolitonSBOX.cer
  certutil -addstore "Root"           .\dist\windows\SolitonSBOX.cer
  ```
- Confirm the MSIX is signed (`Get-AuthenticodeSignature`).
- Enable Developer Mode on Windows.
- Remove any previous app versions (`Get-AppxPackage *SBoxApp* | Remove-AppxPackage`).

### signtool.exe not found

Install the Windows SDK (`winget install --id Microsoft.WindowsSDK -e`) or adjust the script to point directly at your SDK installation.

### macOS script exits with missing prerequisites

Follow the instructions printed by the script (install Xcode tools, run `dotnet workload install maui`, etc.) and rerun.
