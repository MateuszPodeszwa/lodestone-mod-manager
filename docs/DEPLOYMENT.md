# Deploying Lodestone (a first-timer's guide)

This explains, in plain terms, how Lodestone gets onto other people's PCs and how it **updates itself**
after you make a change — no re-emailing, no "please reinstall."

If you only read one thing: **to ship a new version, you push a git tag like `v0.1.1`. That's it.**
GitHub builds it, packages it, and every installed copy picks it up on its next launch.

---

## 1. The mental model

```
   you commit a change            GitHub Actions does the work           your users
   ────────────────────           ───────────────────────────           ──────────
   git tag v0.1.1         ──►   build + test + Velopack package   ──►   app checks GitHub
   git push origin v0.1.1        publish a GitHub Release                on next launch,
                                 (Setup.exe + Portable.zip + feed)       downloads, restarts
                                                                         on the new version
```

Three moving parts, all already wired up in this repo:

| Part | What it is | Where |
|------|------------|-------|
| **Velopack** | The library that builds the installer, the update feed, and does the in‑app updating. | `src/Lodestone.App` (`Program.cs`, `Services/VelopackAppUpdater.cs`) |
| **Release workflow** | The GitHub Actions job that builds + publishes when you push a `v*` tag. | `.github/workflows/release.yml` |
| **The feed** | A **GitHub Release** on this (public) repo. Clients read it anonymously. | `https://github.com/MateuszPodeszwa/LodestoneModManager/releases` |

> **Why the repo must be public:** end users' PCs fetch the update files from GitHub **without logging
> in**. A private repo refuses anonymous downloads, so both the download links *and* auto‑update would
> silently fail. This repo is public for exactly that reason. (The source is MIT‑licensed and the only
> embedded key is the **public** supporter key — nothing secret is exposed.)

---

## 2. What your users download

Each release publishes two files. Both are **self‑contained** — the user does **not** need to install
.NET 10 or anything else.

| File | What it is | Best for |
|------|-----------|----------|
| **`Lodestone-win-Setup.exe`** | A proper Windows installer. Installs to `%LocalAppData%\Lodestone`, adds a Start‑menu shortcut, and **registers for auto‑update**. | **Almost everyone.** This is the one that updates itself. |
| **`Lodestone-win-Portable.zip`** | A standalone folder — unzip and run `Lodestone.exe`. No install, leaves no traces. | USB sticks, locked‑down PCs, "I don't want to install anything." |

**Important nuance about auto‑update:** the **installer** version is the one that updates itself
automatically. The **portable zip** is a frozen snapshot — to update it, the user downloads the newer
zip and replaces the folder. So tell most people to grab the **Setup.exe**.

The permanent "always points at the newest version" download link to share is:

```
https://github.com/MateuszPodeszwa/LodestoneModManager/releases/latest
```

---

## 3. Cutting a release (the whole process)

### One‑time setup
Already done — nothing to configure. The workflow uses GitHub's built‑in token, so there are no secrets
to add for basic releases. (Optional code‑signing is covered in §5.)

### Every release — 3 commands
From the repo root, with your change already committed and pushed to `main`:

```powershell
# 1. (optional but recommended) give the release a codename in two places:
#    - in-app banner:      src/Lodestone.Application/Common/ReleaseNames.cs   ["0.1.1"] = "Iron Pickaxe",
#    - website changelog:  website/app.config.ts → releases.names            '0.1.1': 'Iron Pickaxe',

# 2. tag the commit you want to release
git tag v0.1.1

# 3. push the tag — THIS is what triggers the release
git push origin v0.1.1
```

> The website **changelog notes are generated from the commits** in each release (the GitHub
> release body itself is left empty), grouped into NEW / IMPROVED / FIXED from your
> conventional-commit subjects. So clear `feat:` / `fix:` messages = a clean public changelog.

Then watch it build:

```powershell
gh run watch          # live view of the GitHub Actions release job (~3–6 min)
```

When it finishes, a new release appears at `…/releases`, with the `Setup.exe`, the `Portable.zip`, and
the update feed files attached. Installed clients will offer the update the next time they open the app.

> You can also trigger a build from the GitHub website (**Actions → Release → Run workflow**) and type a
> version — handy if you'd rather not tag from the command line.

### Cutting a beta (patrons‑first early access)

To ship a build to supporters first, tag a **pre‑release** version — anything with a semver suffix
(`-beta.1`, `-rc.1`, …):

```powershell
git tag v0.2.0-beta.1
git push origin v0.2.0-beta.1
```

`release.yml` detects the `-` suffix and publishes it as a GitHub **pre‑release** (it adds `--pre` to
the Velopack upload). Only supporters receive it — in the app via **Settings → Mods & updates →
Early access** (the Beta update channel), and on the website via the **Download beta** button on the
Supporter page. Stable users never see it. When you're ready for everyone, cut a normal release
(`v0.2.0`); stable clients pick it up on next launch and beta users roll onto it cleanly. The full
mechanics and gating caveats are in **[SUPPORTERS.md](SUPPORTERS.md#early-access-beta-builds)**.

---

## 4. How "make a change → it auto‑updates" actually works

This is the part you asked about. Once a user has installed Lodestone via `Setup.exe`:

1. **You ship `v0.1.1`** with the 3 commands above.
2. When the user **opens Lodestone**, the app asks GitHub "is there a release newer than mine?"
   (It also re‑checks when they hit the in‑app **Check for updates** button on Home / in Settings.)
   There is **no background service** — the check happens on launch and on demand only, by design.
3. If a newer release exists, Velopack **downloads only the difference** (a small delta, not the whole
   60 MB), then swaps it in and **restarts the app on the new version**.
4. The user never visits GitHub, never re‑downloads manually, and you never email anyone.

So your release cadence is simply: *change code → `git tag` → `git push`*. Everything after that is
automatic for installed users.

**Versioning** follows [SemVer](https://semver.org): `MAJOR.MINOR.PATCH`.
- Bug fix → bump PATCH (`0.1.0` → `0.1.1`)
- New feature, nothing broken → bump MINOR (`0.1.1` → `0.2.0`)
- Breaking change → bump MAJOR (`0.x` → `1.0.0`)

The tag (`vX.Y.Z`) is the single source of truth — the app's reported version comes straight from it.

---

## 5. The SmartScreen warning (and code signing)

Because the build is **not code‑signed**, the very first time someone runs `Setup.exe` Windows may show
a blue **"Windows protected your PC"** SmartScreen dialog. It is **not an error** — the user clicks
**More info → Run anyway** once, and it never asks again on that machine. Worth a one‑line note on your
download page so people aren't scared off.

To remove the warning entirely you need a CA‑trusted **code‑signing certificate**. Because Lodestone is
open source, you can get one **free** from **SignPath Foundation** — the full plan, the required public
signing policy, and the ready‑to‑apply workflow steps are in **[CODE-SIGNING.md](CODE-SIGNING.md)**.
(A self‑signed cert does **not** work — Windows treats it as unsigned.) Until signing is switched on,
unsigned releases work fine; SmartScreen reputation also improves on its own as more people install.

---

## 6. Troubleshooting

| Symptom | Likely cause / fix |
|---------|--------------------|
| Release workflow didn't start | The tag must match `v*` (e.g. `v0.1.1`) **and** be pushed (`git push origin v0.1.1`). Pushing the commit alone does nothing. |
| Workflow failed on "Restore & test" | A test broke. Run `dotnet test Lodestone.slnx -c Release` locally, fix, re‑tag (see below). |
| "Check for updates" finds nothing on a dev build | Expected. Auto‑update only works on a copy installed via `Setup.exe`, not on `dotnet run` or the portable zip. |
| Need to redo a release | Delete the bad tag + release, then re‑tag: `git push --delete origin v0.1.1 && gh release delete v0.1.1 -y` then tag again. Avoid re‑using a version number that real users already installed. |
| User on the portable zip didn't get the update | Portable = manual. Point them at `…/releases/latest` to re‑download, or to `Setup.exe` for auto‑updates going forward. |

---

## 7. Quick reference

```powershell
# Ship a new version
git add -A && git commit -m "fix: whatever you changed"
git push
git tag v0.1.1
git push origin v0.1.1
gh run watch

# Ship a beta to supporters first (pre-release tag → GitHub pre-release)
git tag v0.2.0-beta.1
git push origin v0.2.0-beta.1
# …later, promote it to everyone:
git tag v0.2.0 && git push origin v0.2.0

# Build the installer + zip locally (to test packaging without releasing)
dotnet publish src/Lodestone.App/Lodestone.App.csproj -c Release -r win-x64 --self-contained true -o publish /p:Version=0.1.1
dotnet tool install -g vpk --version 1.2.0
vpk pack --packId Lodestone --packVersion 0.1.1 --packDir publish --mainExe Lodestone.exe --packTitle "Lodestone Mod Manager" --packAuthors "Mateusz Podeszwa" --icon src/Lodestone.App/Assets/lodestone.ico
# → artifacts land in ./Releases  (Setup.exe, Portable.zip, *-full.nupkg)
```

See also **[HANDOFF.md](HANDOFF.md)** for maintainer setup (Patreon, supporter keys, CurseForge, signing).
