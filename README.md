<div align="center">

<img src="docs/logo.png" alt="AccSwap logo" width="160" />

# AccSwap

**Switch between accounts on Steam, Epic and other game launchers, fast and safely.**

[![Release](https://img.shields.io/github/v/release/fredericjasmin/AccSwap?style=flat-square&logo=github)](https://github.com/fredericjasmin/AccSwap/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-blueviolet?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue?style=flat-square&logo=windows)](https://github.com/fredericjasmin/AccSwap/releases/latest)

[English](README.md) · [Español](README.es.md)

</div>

---

<img src="docs/screenshot.png" alt="AccSwap main window" width="720" />

## What is AccSwap?

AccSwap is a free, open-source account switcher for Windows game launchers. It saves the login session of each account on your machine, so switching accounts is one click: no re-entering passwords, no 2FA prompts, no email codes.

Everything stays on your PC: AccSwap has no servers, no accounts and no telemetry.

## Features

- One-click account switching per platform, with the active account highlighted
- "Add account" captures your current session; "Save session" stores the account you're logged in with
- System tray icon with the full account list: switch without opening the window (global hotkey `Ctrl+Alt+G`)
- Automatic launcher detection, including installations on secondary drives, with manual path override per platform
- Auto-update: AccSwap checks GitHub Releases and updates itself
- Export / import accounts and settings between machines (import is additive: it never deletes what you already have)
- Optional password protection for account snapshots (PBKDF2 + AES-GCM)
- English and Spanish, picked from your system language
- Single portable `.exe`, no installer needed

## Supported platforms

| Support | Platforms |
|---|---|
| Dedicated (full switching logic) | Steam, Epic Games Store |
| Catalog (session snapshot + restore) | Riot Games, Ubisoft Connect, Battle.net, EA Desktop, GOG Galaxy, Discord, Escape from Tarkov, Roblox |

Catalog platforms are verified by automated tests with simulated folders. If one misbehaves on your setup, its definition in `%APPDATA%\AccSwap\platforms.json` can be fixed without recompiling.

## Download

Grab `AccSwap-vX.Y.Z.exe` from the [latest release](https://github.com/fredericjasmin/AccSwap/releases/latest) and run it. It's a portable executable: no installation, and it can update itself from GitHub.

> [!NOTE]
> The executable is not code-signed yet, so Windows SmartScreen may warn the first time. Click **More info → Run anyway**. See the [roadmap](#roadmap).

## How to use

1. Open AccSwap and pick a platform tab (Steam, Epic, ...).
2. Log into the launcher with the account you want to add.
3. In AccSwap, click **Add account**: the session is captured locally.
4. Log into the launcher with another account and repeat.
5. Click **Switch** on any card to jump to that account. AccSwap closes the launcher, restores the session and relaunches it.

## Build from source

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet), Visual Studio 2026 (optional).

```bash
git clone https://github.com/fredericjasmin/AccSwap.git
cd AccSwap

dotnet build AccSwap.slnx               # build everything
dotnet test                          # run the test suite

dotnet publish AccSwap.App/AccSwap.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Roadmap

- [ ] Code signing (to remove the SmartScreen warning)
- [ ] Installer (NSIS/WiX) alongside the portable exe
- [ ] More launchers: Origin, Rockstar, PS Remote Play, Magic Arena
- [ ] Per-account desktop shortcuts

## Contributing

Issues and pull requests are welcome. For bugs, include your Windows version, the platform and what you expected vs. what happened.

## License

[MIT](LICENSE) © Frederic Jasmin
