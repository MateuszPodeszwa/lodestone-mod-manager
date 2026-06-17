# Lodestone website (`lodestonemc.net`)

The official marketing + supporter site for **Lodestone**, the friendly Minecraft mod
manager. It's a single [Nuxt 3](https://nuxt.com) app (Vue 3 + Tailwind + GSAP) with a
small server layer that:

- pulls the **version, changelog, downloads and checksums** live from the app's GitHub
  Releases, so the site is always truthful and updates itself when you cut a release;
- verifies patrons via **Patreon OAuth** and mints **real supporter codes** the desktop
  app accepts (signed with your ECDSA key);
- tracks supporters (tier, pledge, key-generation counts, beta access) in **Postgres**.

> **The app deep-links to this site.** `DonateViewModel` opens `/supporter` (claim a code)
> and `/support` (priority support). Both pages exist here.

---

## 1. Editing content (no coding)

Almost all text, links and pricing live in **[`app.config.ts`](./app.config.ts)** — open it,
edit the strings, save. That covers the hero, feature grid, tutorial steps, the home trust
strip, support tiers, FAQ, release codenames (`releases.names`), footer, and the
GitHub/Patreon/Discord links.

Things you **don't** edit by hand (they come from GitHub automatically):

- the current **version number** (hero, download page, changelog);
- the **changelog** entries — generated from the commits in each release;
- **download links** and **SHA-256 checksums**.

The changelog is built from the **commits between release tags** (the GitHub Release bodies
are empty): conventional-commit subjects (`feat:`, `fix:`, `perf:` …) become the
NEW / IMPROVED / FIXED lines, while housekeeping types (`docs`, `ci`, `chore`) and non-app
scopes (`website`, `design`) are filtered out. So cutting a `vX.Y.Z` release populates its own
notes — just write clear commit messages. The only editorial bit is the optional per-release
**codename** badge (e.g. "Spawn Point"), set in `app.config.ts` under `releases.names`.

### Discord (work in progress)

Discord is greyed out with a "coming soon" tooltip everywhere until you paste an invite URL
into `links.discord` in `app.config.ts`. One edit enables it site-wide.

---

## 2. Local development

```bash
cd website
cp .env.example .env       # then fill in values (see below)
npm install
npm run dev                # http://localhost:3000
```

You don't need a database or Patreon keys just to view the site — those features degrade
gracefully when their env vars are missing.

Useful scripts:

| Script | What it does |
| --- | --- |
| `npm run dev` | Dev server with HMR |
| `npm run build` | Production build (`prisma generate` + `nuxt build`) |
| `npm run start` | Apply DB migrations (if `DATABASE_URL` set) then serve `.output` |
| `npm run db:studio` | Browse the database (Prisma Studio) |
| `node scripts/gen-icons.mjs` | Regenerate favicons/OG from the app's source PNG (needs `npm i sharp --no-save` first) |

---

## 3. Environment variables

Set these in **Railway → your service → Variables** (and in `.env` for local dev). Full
descriptions are in [`.env.example`](./.env.example).

| Variable | Required | Purpose |
| --- | --- | --- |
| `NUXT_SESSION_PASSWORD` | ✅ | 32+ random chars; encrypts the login cookie. `openssl rand -base64 32` |
| `NUXT_SUPPORTER_PRIVATE_KEY_B64` | ✅ for key gen | Contents of `keys/supporter.private.b64` (the app's signing key) |
| `NUXT_PATREON_CLIENT_ID` / `_SECRET` | ✅ for sign-in | Patreon OAuth client credentials |
| `NUXT_PATREON_REDIRECT_URI` | ✅ for sign-in | `https://lodestonemc.net/api/auth/patreon/callback` |
| `NUXT_PATREON_CAMPAIGN_ID` | optional | Restrict eligibility to your campaign |
| `NUXT_BETA_THRESHOLD_CENTS` | optional | Pledge (cents) that unlocks the website's beta download (default `700`) |
| `NUXT_GITHUB_REPO` | optional | Defaults to `MateuszPodeszwa/LodestoneModManager` |
| `NUXT_GITHUB_TOKEN` | optional | Raises the GitHub API rate limit |
| `DATABASE_URL` | auto on Railway | Postgres connection (the plugin injects it) |
| `NUXT_PUBLIC_SITE_URL` | recommended | Canonical URL, e.g. `https://lodestonemc.net` |

---

## 4. Supporter keys — how it works

The desktop app verifies **offline, signed codes** of the form
`base64url(payload).base64url(signature)` where the payload is
`{"v":1,"k":"supporter","h":<holder>,"iat":<unix-secs>}` signed with **ECDSA P-256 / SHA-256
(IEEE-P1363)**. This site signs exactly that shape with the **private key matching the public
key embedded in the app** (`SupporterKeys.DefaultPublicKey`).

- Codes are issued only to **active, paying** patrons — former/declined patrons and free
  followers don't qualify (the `NUXT_PATREON_OWNER_*` allowlist is the one exception).
- The private key lives **only** in `NUXT_SUPPORTER_PRIVATE_KEY_B64` (a server secret). It is
  never sent to the browser.
- A code is valid to redeem for **1 hour** (enforced by the app). The site also enforces a
  **1-hour regenerate cooldown** per patron, shown as a live countdown.
- We store generation **counts/timestamps**, never the code strings — patrons are told they're
  responsible for their key and asked not to redistribute it.

You can sanity-check that a generated code is accepted by the real app:

```bash
# from the repo root
dotnet run --project src/Lodestone.Cli -- verify --pub "@keys/supporter.public.b64" --code <code>
# → VALID  holder=…  (redeemable for 1h after issue)
```

> If the private key ever leaks: run `dotnet run --project src/Lodestone.Cli -- keygen`,
> update `SupporterKeys.DefaultPublicKey`, ship an app update, and set the new private key here.
> Old codes simply stop verifying.

### Patreon OAuth setup

1. Create a client at <https://www.patreon.com/portal/registration/register-clients>.
2. Add the redirect URI `https://lodestonemc.net/api/auth/patreon/callback`.
3. Copy the Client ID/Secret into the env vars above.
4. (Optional) Set `NUXT_PATREON_CAMPAIGN_ID` to your campaign's numeric id to restrict eligibility.

---

## 5. Deploy to Railway

1. **New Project → Deploy from GitHub repo**, pick this repository.
2. In the service **Settings → Build**, set **Root Directory** to `website`.
   (Railway then uses this folder's `package.json` / `railway.json`.)
3. In **Settings → Build**, set **Watch Paths** to `/website/**`. This site lives in a
   monorepo alongside the desktop app — Watch Paths makes Railway redeploy **only** when
   website files change, so pure app commits don't trigger a website rebuild. (Root Directory
   controls *what is built*; Watch Paths controls *what triggers a deploy* — set both.)
4. **Add a Postgres database**: *New → Database → PostgreSQL*. Railway injects `DATABASE_URL`
   into your service automatically.
5. Add the **environment variables** from the table above (at minimum
   `NUXT_SESSION_PASSWORD`; add the Patreon + signing-key vars to enable the supporter flow).
6. Deploy. Railway runs `npm run build`, then `npm run start` — which applies the Prisma
   migration and boots the server. The healthcheck hits `/`. Every later `git push` that
   touches `website/` auto-deploys.
7. Point your domain (`lodestonemc.net`) at the service under **Settings → Networking →
   Custom Domain**, and set `NUXT_PUBLIC_SITE_URL` to the final URL.

First deploy with no GitHub releases yet? The site still shows a truthful fallback
(`v0.1.0 "Spawn Point"`) and links to the GitHub releases page.

> **You rarely need to redeploy.** Version, changelog, downloads and checksums are fetched from
> the GitHub Releases API at runtime — cutting a `vX.Y.Z` release updates the live site within a
> few minutes with **no push and no redeploy**. Only code or `app.config.ts` content changes
> require a push (→ auto-deploy via Watch Paths).

---

## 6. Project structure

```
website/
├─ app.config.ts          ← editable site content (start here)
├─ nuxt.config.ts         ← modules, SEO defaults, runtime config
├─ pages/                 ← /, /download, /changelog, /supporter, /support, /report
├─ components/            ← Nav, Footer, AppWindow (in-app mock), CursorSword, …
├─ composables/           ← useToast, useFormat
├─ plugins/gsap.client.ts ← scroll-reveal animations
├─ server/
│  ├─ api/                ← releases, checksums, download, auth/patreon, me, key/generate
│  ├─ routes/             ← robots.txt, sitemap.xml
│  └─ utils/              ← github, patreon, supporterCode (signing), db, keylock
├─ prisma/schema.prisma   ← Supporter + KeyGeneration models
└─ public/                ← favicons/OG (from the app icon), stone-sword cursor
```

---

Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.
