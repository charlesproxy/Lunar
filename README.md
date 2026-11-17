![Lunar — VALORANT tracker](assets/lunar-banner.svg)

# Lunar

**A VALORANT companion for Windows.** Review match history and performance, follow your live lobby, check the daily store, browse crosshairs, and keep up with friends—all in one place.

[![Latest release](https://img.shields.io/github/v/release/charlesproxy/Lunar?label=latest%20release&color=7c3aed)](https://github.com/charlesproxy/Lunar/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows-7c3aed)

### What’s inside

- Match history, rank, and performance summaries
- Live Match view and the Alt + T overlay
- Friends, daily store, and crosshair library
- Automatic signed updates for the installed app

## Install

Download **Lunar-Setup** from [Releases](https://github.com/charlesproxy/Lunar/releases) and run it. Requires .NET Framework 4.8 or later.

The released MSI is ready to use and does not ask you to enter a Tracker key. Tracker data currently comes from HenrikDev, so all MSI users share the provider’s quota and availability. Live Match requires VALORANT to be running. Press **Alt + T** to toggle the overlay in windowed fullscreen.

### Data and API

Lunar currently uses HenrikDev for public profile and match-history data; it does not host its own stats API. A Lunar-operated service could cache and serve data, but it would still need an authorized upstream data source. Riot’s official VALORANT match-data access requires an approved production API application and Riot Sign On (RSO), with player opt-in. Riot’s policy also restricts opponent scouting and certain real-time overlays. See the [VALORANT developer policy](https://developer.riotgames.com/docs/valorant) before pursuing an official integration.

Source builds do not include the released MSI’s provider configuration. Set `LUNAR_HENRIK_KEY` or enter a HenrikDev key once in Lunar; locally saved keys are encrypted for the current Windows account.

## Build from source

Run `build.ps1` in PowerShell. For an MSI, run `build-installer.ps1 -WixPath <WiX 3.14 folder>`. Release builds can optionally use Obfuscar with `-ObfuscarPath <Obfuscar.Console.exe>`.

Assets are from [Valorant-API](https://valorant-api.com/). Crosshair credits are in [`assets/crosshairs.json`](assets/crosshairs.json).
