# Supporters & donations

Lodestone is **free, and always will be**. Donations are optional and **never gate functionality** —
supporters get cosmetic/convenience perks and our thanks. There is **no payment processor and no backend
in the app**: pledging happens on Patreon, a website verifies the pledge and mints a short-lived code,
and that code unlocks the perks **offline**.

## How it works (for users)

1. **Join on Patreon** on any **paid** tier — you must be an **active** patron (former/declined
   patrons and free followers don't qualify).
2. **Open our website** and sign in with Patreon to verify your pledge.
3. The site **generates a code** that is valid for **one hour**.
4. Paste it into **Support → "Redeem your supporter code"**. The app verifies it offline and unlocks
   your perks **permanently** (until you remove the code or uninstall).

### What a code unlocks (cosmetic / convenience only)

- A **Supporter badge** in the title bar.
- **Exclusive accent themes** (Settings → Appearance; entitlement: `SupporterService.CanUseExtraThemes`).
- **Early access** to beta builds (Settings → Mods & updates; entitlement: `SupporterService.CanUseBetaChannel`).
- **Priority support** (a link to the support channel).

Core mod-managing — install, update, compatibility checks, everything — is always free.

## Early access (beta builds)

Beta builds let supporters try updates before everyone else. It runs off the **same GitHub
Releases feed**, split by whether a release is marked a *pre-release*:

- **Cutting a beta:** tag a pre-release version (e.g. `v1.3.0-beta.1`). `release.yml` sees the
  `-` suffix and publishes it as a GitHub **pre-release** (it passes `--pre` to Velopack). Promote
  it to everyone later by cutting a normal `v1.3.0` release — stable clients roll onto it then.
- **In the app:** a supporter turns on **Settings → Mods & updates → Early access**
  (`SupporterService.CanUseBetaChannel`). Velopack's feed then includes pre-releases
  (`GithubSource(prerelease: true)`); everyone else stays on stable and never sees the beta.
- **On the website:** the Supporter page shows a **Download beta** button to entitled patrons,
  resolving to the latest pre-release installer via `/api/download/beta`.

> **Two caveats worth knowing:**
> 1. **It's soft-gated, not secret.** The repo is public, so a pre-release is still downloadable
>    straight from the GitHub Releases page. The gates stop the normal in-app/website paths, not a
>    determined manual download. For true privacy, serve beta packages from an authenticated
>    endpoint instead of public GitHub.
> 2. **The app can't enforce a tier.** The signed code carries only the holder + issue time (no
>    pledge amount), so in-app early access unlocks for **any** supporter, whereas the website's
>    Download beta button requires the `NUXT_BETA_THRESHOLD_CENTS` pledge (default $7). To gate the
>    in-app channel by tier too, you'd have to encode an entitlement level into the signed code.

## Security model (offline, no backend)

Codes are signed tokens: `base64url(payload).base64url(signature)`, where the payload is
`{"v":1,"k":"supporter","h":<holder>,"iat":<unix-seconds-UTC>}`, signed with **ECDSA P-256 / SHA-256**.
The app verifies them with an **embedded public key** (`SupporterKeys.DefaultPublicKey`); only the public
key ships, the private key stays with the maintainer/website.

- **1-hour activation window.** The app accepts a code only within one hour of its `iat`. The policy lives
  app-side (`SupporterService.ActivationWindow`), so a leaked code can't extend its own life — and a short
  window means a shared code dies quickly.
- **Permanent after activation.** Once redeemed, status is granted with no recurring expiry.
- **Tamper-resistant at rest.** The app stores the *signed code itself* and **re-verifies its signature on
  every load** — editing `entitlements.json` (or flipping a flag) can't fake supporter status without a
  genuinely signed code. (Binary-patching the client is unpreventable in any offline scheme; signing
  defeats the realistic threat, which is data tampering.)
- **Uninstall clears it.** A Velopack uninstall hook deletes the token, so a reinstall requires a fresh code.

> The website must sign exactly the payload shape above with the private key matching the embedded public
> key. Until the website exists, the `lodestone` CLI mints codes the same way (below).

## Maintainer tooling (CLI)

### One-time setup

```powershell
# Generate a key pair (do this once)
dotnet run --project src/Lodestone.Cli -- keygen
#  → PRIVATE=...   (keep secret — save under keys/, which is git-ignored)
#  → PUBLIC=...    (paste into src/Lodestone.Infrastructure/Supporter/SupporterKeys.cs)
```

### Issuing a code

```powershell
dotnet run --project src/Lodestone.Cli -- issue `
  --key "@keys/supporter.private.b64" `
  --holder "patron@example.com" [--issued 2026-06-16T12:00:00Z]
# → prints the code (valid to redeem for 1 hour after --issued, default now)

# Sanity-check any code against a public key:
dotnet run --project src/Lodestone.Cli -- verify --pub "@keys/supporter.public.b64" --code <code>
```

> ⚠️ **Never commit the private key.** `keys/` is git-ignored. If it ever leaks, run `keygen` again,
> replace `SupporterKeys.DefaultPublicKey`, and ship an update — old codes simply stop verifying.

## Configuring the links

The links live in `DonateViewModel` (`src/Lodestone.App/ViewModels/DonateViewModel.cs`):

- `PatreonUrl` → `https://www.patreon.com/c/mateuszpodeszwa`
- `WebsiteUrl` → `https://lodestonemc.net/supporter` (Patreon login + code generation)
- `PrioritySupportUrl` → `https://lodestonemc.net/support`

The two `lodestonemc.net` paths assume that site is live — the domain is registered but not yet
deployed, so those pages must exist before the supporter flow works end to end.
