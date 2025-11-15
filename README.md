# Soliton SBOX

A cross‑platform (Windows + macOS) MAUI application for managing the SPL SBOX runtime. This document explains how to install and run the app as an end user. For developer packaging instructions see [docs/distribution.md](docs/distribution.md).

## System Requirements

| Platform | Minimum |
| --- | --- |
| **Windows** | Windows 10 21H2 or later (x64), .NET Runtime prerequisites installed automatically by MSIX. |
| **macOS** | macOS 13 Ventura or later (x64 or Apple Silicon running Rosetta) when using the provided `.pkg`. |

## Installing on Windows

You will receive two files:

* `SBoxApp_1.0.0.0_x64.msix` (Windows installer)
* `SBoxApp_1.0.0.0_x64.cer` (signing certificate)

1. Open **PowerShell as Administrator** and trust the signing certificate in both required stores:
```powershell
certutil -addstore "TrustedPeople" .\dist\windows\SBoxApp_1.0.0.0_x64.cer
certutil -addstore "Root"           .\dist\windows\SBoxApp_1.0.0.0_x64.cer
```
2. If you previously installed the app, remove it:
   ```powershell
   Get-AppxPackage *SBoxApp* | Remove-AppxPackage
   ```
3. Turn on **Developer Mode** (Settings → Privacy & security → For developers → Developer Mode). This allows self-signed MSIX packages to be installed.
4. Install the MSIX:
   ```powershell
   Add-AppxPackage .\dist\windows\SBoxApp_1.0.0.0_x64.msix
   ```

## Installing on macOS

**Important:** We distribute the macOS app as source code or unsigned builds because network-server entitlements require an Apple Developer certificate. Please install dependencies and build/run locally.

### Prerequisites

1. Install Xcode (from the App Store) and command-line tools:
   ```bash
   sudo xcode-select --switch /Applications/Xcode.app/Contents/Developer
   sudo xcodebuild -runFirstLaunch
   ```
2. Install .NET 8 SDK

   ```bash
   dotnet --version  # Skip cleanup if this already shows 8.0.x
   ```

   **If no .NET is installed**

   ```bash
   brew install --cask dotnet-sdk@8
   sudo ln -sfn /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
   ```

   **If other versions are present**

   ```bash
   pkill -f dotnet || true
   sudo rm -rf /usr/local/share/dotnet ~/.dotnet ~/.nuget /Library/Frameworks/dotnet.framework /Library/Receipts/dotnet*
   sudo rm -rf /private/var/db/receipts/com.microsoft.dotnet* /opt/homebrew/Cellar/dotnet* /opt/homebrew/Caskroom/dotnet*
   brew uninstall --force --cask dotnet-sdk dotnet-sdk@8 dotnet-sdk@9 || true
   brew cleanup -s || true
   brew install --cask dotnet-sdk@8
   ```

   You can also download the official 8.0 installer from Microsoft if Homebrew is not available.

   Verify:

   ```bash
   dotnet --version
   dotnet --list-sdks
   ```

3. Install MAUI workloads:
   ```bash
   sudo dotnet workload install maui maui-maccatalyst
   dotnet workload list
   ```

### Build/Run the app

Clone the repo and run:
```bash
git clone https://github.com/solitontech/sbox.git
cd sbox
dotnet build -t:Run -f net8.0-maccatalyst -r maccatalyst-arm64
```
This runs the MAUI app with development entitlements, so the bot gateway can bind to localhost ports.


## Configuring SBOX

Open the app and navigate to **Settings**:

| Field | Description | Default |
| --- | --- | --- |
| Player Email | Email used to register with SPL server. | _empty_ |
| SPL API Key | Key associated with your SPL account. | _empty_ |
| Team Id (optional) | Optional identifier for your team. | _empty_ |
| Bot Gateway IP/Port | Where your bot connects (usually localhost:2025). | `127.0.0.1:2025` |
| Server IP/Port | SPL server address. | `192.168.1.235:50500` |

Make any edits, click **Save & Apply**, and the runtime restarts with the new configuration. Settings are stored per user (macOS: `~/Library/Application Support/Soliton/SBox/settings.json`, Windows: `%APPDATA%\Soliton\SBox\settings.json`).

## Using SBOX

1. Go to the **Home** tab.
2. Press **Start SBOX**. The status tiles indicate Bot, Server, and Game Engine connectivity.
3. Once Bot and Server show *Online*, use SBOX Live to request a game. Logs stream in real time via the **Logs** tab.
4. To stop the runtime press **Stop SBOX**. The app keeps settings for the next launch.

## Creating New Builds

Developers or release engineers should follow [docs/distribution.md](docs/distribution.md) for packaging instructions, certificate management, and signing verification for both Windows (MSIX) and macOS (`.pkg` + zipped publish). That document also covers the publish scripts and prerequisites.
