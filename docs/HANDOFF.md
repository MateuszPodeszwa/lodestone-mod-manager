# Maintainer handoff

Lodestone runs today as-is. These optional steps connect it to your real accounts/assets and harden
releases. Each is independent.

## 1. Patreon link
Set your Patreon URL in `DonateViewModel.PatreonUrl`
(`src/Lodestone.App/ViewModels/DonateViewModel.cs`). The **Support us** buttons open it.

## 2. Supporter codes (already wired)
A real key pair was generated; the **public** key is embedded in `SupporterKeys.DefaultPublicKey`
and the **private** key is under `keys/` (git-ignored). Issue codes with the CLI — see
[SUPPORTERS.md](SUPPORTERS.md). To rotate keys, run `lodestone keygen`, replace the public key, ship
an update. Keep `keys/` backed up somewhere safe and out of version control.

## 3. CurseForge (optional second source)
Modrinth works with no key. To enable the CurseForge fallback, obtain an
[Eternal API key](https://console.curseforge.com/) and pass it through `LodestoneOptions.CurseForgeApiKey`
in `AddLodestone(...)` (App composition), then implement the calls in `CurseForgeModSource`
(currently a configured-stub that reports "not configured" so it's skipped safely).

## 4. Code signing (optional, recommended for distribution)
Unsigned builds work but may trip SmartScreen on first run. Add a code-signing certificate as CI
secrets and the `--signParams` argument in `release.yml` (a placeholder comment marks the spot).

## 5. Cutting a release
Tag a commit `vX.Y.Z` and push the tag. `release.yml` tests, publishes a self-contained win-x64
build, packages it with Velopack and publishes a GitHub Release — the feed that installed clients
update from. Example:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## 6. App icon (cosmetic polish)
No custom `.ico` ships yet (the exe uses the default). Add one and set `<ApplicationIcon>` in
`Lodestone.App.csproj`; it will also become the title-bar/tray icon.
