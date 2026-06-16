<div align="center">

# 🧭 Lodestone — Minecraft Mod Manager

**The simplest way to install and manage Minecraft mods, resource packs and shaders.**
No profiles. No config files. No fuss.

[![CI](https://github.com/MateuszPodeszwa/LodestoneModManager/actions/workflows/ci.yml/badge.svg)](https://github.com/MateuszPodeszwa/LodestoneModManager/actions/workflows/ci.yml)
&nbsp;·&nbsp; Windows · .NET 10 · WPF · MIT

</div>

---

Lodestone is a fast, lightweight native Windows app for Minecraft (Java Edition). Drag a `.jar`
onto the window and it is installed instantly into the right place for your selected game version.
Browse thousands of mods from Modrinth, see at a glance which mods have a missing dependency or a
conflict, and keep everything up to date — all without ever touching a config file.

> Lodestone is **free, and always will be**. Every feature is available to everyone; supporters get
> a few cosmetic perks and our thanks. See [Supporters](#-supporters).

## ⬇️ Download & install

**[⬇ Get the latest release →](https://github.com/MateuszPodeszwa/LodestoneModManager/releases/latest)**

Pick one — both are self-contained, so there's **nothing else to install** (no .NET, no runtimes):

- **`Lodestone-win-Setup.exe`** — recommended installer; **updates itself automatically** from then on.
- **`Lodestone-win-Portable.zip`** — standalone, no install (to update, just download the newer zip).

### How to install (Setup.exe)

1. Download **`Lodestone-win-Setup.exe`** from the [latest release](https://github.com/MateuszPodeszwa/LodestoneModManager/releases/latest).
2. Run it. Windows may show a blue **"Windows protected your PC"** screen — this is expected because the
   app isn't code-signed yet, **not** a virus warning. Click **More info → Run anyway** (just once).
3. Lodestone installs and launches itself. You're done — there's no setup wizard to click through.

From then on it **keeps itself up to date**: when a new version is released, Lodestone downloads and
applies it on the next launch. You'll never need to reinstall.

> **Prefer not to install?** Download **`Lodestone-win-Portable.zip`**, unzip it anywhere (e.g. a USB
> stick), and run `Lodestone.exe`. The portable version doesn't auto-update — grab the newer zip when
> you want the latest.

Maintainers: how releases and auto-update work is documented in **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**.

## ✨ Features

- **Drag & drop install** — drop `.jar` / `.zip` / `.litemod` / `.mcpack` anywhere on the window;
  the type (mod / resource pack / shader) is auto-detected and the file lands in the correct folder
  for the **currently selected game version**.
- **Browse mods** — search Modrinth (CurseForge pluggable), filter by category, sort by downloads /
  followers, and install with one click.
- **My Content** — per-version "profiles", enable/disable without deleting, uninstall, search.
- **Compatibility & dependency checks** — every item in the list is scanned; a clear symbol appears
  next to its name when it **requires a missing library**, **conflicts with another mod**, is
  **built for a different game version/loader**, or is **duplicated**. Hover for the full reason.
- **Updates on your terms** — Lodestone checks for mod updates **on start and when you refresh**
  (never with a background daemon). Optional auto-update keeps enabled mods current.
- **Settings that do something** — game directory, default loader, concurrent downloads, update &
  notification behaviour, CurseForge fallback, close-to-tray. Every toggle is wired to real logic.
- **App auto-update** — ships with [Velopack](https://velopack.io); new releases install themselves.
- **Lightweight by design** — no always-on services; the process ends when you close the window
  (unless you opt into the tray). Your `.minecraft` is only ever changed by an action you take.

## 🏗️ Architecture (at a glance)

Clean / Onion layering with MVVM at the edge. Dependencies always point inward, so the core logic
is fully unit-testable and a future macOS port only needs to swap the UI layer.

```
Lodestone.Domain          pure entities, value objects, rules — no dependencies
Lodestone.Application     ports (interfaces) + use-cases + the compatibility engine
Lodestone.Infrastructure  adapters: Modrinth API, archive readers, file system, settings, updater
Lodestone.App  (WPF)      views + viewmodels + DI composition root
Lodestone.Cli             headless surface (handy for scripting and integration tests)
```

The codebase intentionally demonstrates a broad set of patterns beyond Dependency Inversion —
Strategy, Factory, Chain-of-Responsibility, Specification, Decorator, Adapter, Repository,
Result/Railway, Options, Observer, Null-Object, Template-Method, Command and a light Mediator.
See **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** for the full tour and
**[docs/RISK-ANALYSIS.md](docs/RISK-ANALYSIS.md)** for the per-feature failure-mode analysis.

## 🚀 Getting started (developers)

```powershell
# Requires the .NET 10 SDK (see global.json)
dotnet restore
dotnet build
dotnet test                                   # runs the full unit-test suite
dotnet run --project src/Lodestone.App        # launches the app
```

## 📦 Releases & auto-update

Tagging a commit `v*` triggers the release workflow, which packages a Velopack installer and
publishes it to GitHub Releases. Installed clients update themselves from that feed. A plain-English,
first-timer's walkthrough — cutting a release, how auto-update works, SmartScreen, troubleshooting —
is in **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**.

**Maintainer setup** (Patreon link, supporter keys, CurseForge key, signing, cutting a release):
see **[docs/HANDOFF.md](docs/HANDOFF.md)**.

## 💚 Supporters

Donations are handled through Patreon and are **entirely optional**. After pledging you receive a
redeemable code that unlocks **cosmetic-only** perks (a supporter badge, extra accent themes and an
opt-in beta update channel). No payment processing happens inside the app, and **no functionality is
ever gated** behind a donation. See [docs/SUPPORTERS.md](docs/SUPPORTERS.md).

## ⚖️ License

[MIT](LICENSE). Not affiliated with Mojang, Microsoft, Modrinth or CurseForge.
The original UI design lives under [`design/`](design/) for provenance.
