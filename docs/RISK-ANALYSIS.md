# Per-feature risk analysis & mitigation

A required design deliverable: for every feature, the things that can go wrong and how Lodestone
guards against them. Severities: 🟥 data-loss/crash · 🟧 broken feature · 🟨 annoyance.

---

## 1. Onboarding & game auto-detection
| Risk | Sev | Mitigation |
|---|---|---|
| `.minecraft` missing or non-standard (MultiMC/Prism, custom launcher, OneDrive-redirected AppData) | 🟧 | Probe a list of known paths; if none validate, onboarding shows a **Choose folder** fallback. A folder is only accepted after it passes `IGameLocator.Validate` (has `versions/` or `mods/`). |
| User skips onboarding with no valid directory | 🟨 | Every file operation is guarded by `IGameLocator.IsValid`; a non-blocking banner prompts to set the directory; nothing throws. |
| Detected path is read-only / needs elevation | 🟧 | Write probe on selection; explain clearly and let the user pick another location. |

## 2. Drag-and-drop install (local files)
| Risk | Sev | Mitigation |
|---|---|---|
| Unknown / huge / corrupt file dropped | 🟧 | Validate extension **and** sniff the ZIP magic header; cap size; corrupt archives fail with a friendly toast. |
| Type mis-detection (mod vs pack vs shader) | 🟨 | Detect by extension first, then by archive contents (`fabric.mod.json`/`mods.toml` → mod, `pack.mcmeta` → resource pack, top-level `shaders/` → shader). |
| Dropped while "All versions" is selected (no target) | 🟨 | Fall back to the detected latest installed version and toast which version received the file. |
| Duplicate / would overwrite an existing file | 🟥 | Detect by filename + sha512; never silently clobber — keep-both or replace, and the replaced file goes to trash. |
| Multiple files dropped at once | 🟨 | All routed through one bounded install queue with per-file progress. |

## 3. Browse + install (Modrinth, CurseForge pluggable)
| Risk | Sev | Mitigation |
|---|---|---|
| Network down / DNS failure | 🟧 | Typed errors surface as the design's empty/error state; never a crash. Cached results shown when available. |
| Rate limiting (Modrinth requests a descriptive `User-Agent`) | 🟧 | Compliant UA string; retry with exponential backoff + jitter; respect `Retry-After`. |
| API schema drift | 🟧 | Responses parsed through tolerant DTOs + adapter; unknown fields ignored; a parse failure degrades to "no results", logged. |
| Slow/janky search UI | 🟨 | Debounced input, per-keystroke `CancellationToken`, all calls async, virtualized lists. |
| Wrong build installed (game version/loader mismatch) | 🟧 | The version resolver only accepts files matching the active game version + loader before download. |

## 4. Library management (toggle / uninstall / filter / search / profiles)
| Risk | Sev | Mitigation |
|---|---|---|
| File locked because Minecraft is running | 🟧 | File ops return `Result`; on `IOException` we retry with backoff and, if still locked, explain "close Minecraft and retry". |
| Partial state between `library.json` and disk | 🟥 | A reconcile pass on every refresh re-syncs the index to what is actually on disk. |
| Uninstall removes a still-needed file | 🟥 | **Soft delete** to a trash folder first; confirmation dialog; toast. |
| Enable/disable corrupts a file | 🟧 | Toggling only renames `…jar ⇄ …jar.disabled` (atomic, loader-ignored); content is never rewritten. |

## 5. Compatibility & dependency detection (headline extra)
| Risk | Sev | Mitigation |
|---|---|---|
| False positives from incomplete metadata | 🟧 | Missing data is treated as **unknown** (no scary error). Only explicit declarations raise issues. |
| Dependency identity mismatch (local mod id vs Modrinth project id) | 🟧 | A slug/id index maps both ways; unresolved ids are reported as "unknown dependency", not "missing". |
| Transitive dependencies | 🟨 | Rules resolve one level; the modal shows the full declared tree from the source where available. |
| Performance on a large library | 🟨 | Indexes built once per scan; each rule is O(n). |

## 6. Updates (per refresh / per start) + auto-update
| Risk | Sev | Mitigation |
|---|---|---|
| "Latest" is incompatible and breaks the game | 🟥 | Only versions matching the active game version + loader are offered; auto-update is opt-in and still filtered; the prior file is kept in trash for rollback. |
| User expects silent background updates | 🟨 | By explicit design there is no daemon — updates run on start/refresh; a manual control is always present and documented. |
| Update interrupted mid-download | 🟧 | Download to a temp file, verify sha512, then atomically swap; a failed download leaves the old file intact. |

## 7. Settings (each option implemented)
Game directory · default loader · auto-update · notify · concurrent downloads (1–6) · CurseForge
fallback · close-to-tray · app version / check-for-updates.
| Risk | Sev | Mitigation |
|---|---|---|
| Corrupt / missing / older-schema settings file | 🟧 | Options pattern with defaults + validation; **atomic** temp-then-rename writes; a corrupt file is backed up and reset to defaults with a toast. |
| "Concurrent downloads" set absurdly | 🟨 | Clamped to 1–6 (matches the design's stepper) and enforced by the download semaphore. |
| Close-to-tray conflicts with "process must end on close" | 🟨 | **Documented decision:** the feature stays but ships **OFF by default**; even when ON the tray app does *zero* background polling — it only keeps the window resident. With it OFF, closing fully terminates the process. |

## 8. App auto-update & deployment
| Risk | Sev | Mitigation |
|---|---|---|
| A bad update bricks the install | 🟥 | Velopack stages updates atomically and rolls back on failure; updates apply on next launch, never mid-session. |
| SmartScreen / AV flags an unsigned exe | 🟨 | Code-signing is a documented opt-in via CI secrets; reproducible builds reduce false positives. |
| Update feed unreachable | 🟨 | Check-for-updates fails gracefully with a clear message; the app keeps working on the current version. |

## 9. Patreon donate & supporter unlock
| Risk | Sev | Mitigation |
|---|---|---|
| No in-app payment processor, yet must "unlock" | 🟧 | Donate buttons open Patreon in the browser; unlocking uses an **offline signed code** (ECDSA P-256, public key embedded, private key stays with the maintainer). |
| Design says functionality must never be gated | 🟧 | Unlocks are **cosmetic/convenience only** (badge, accent themes, beta channel). Core features remain free. |
| Codes get shared / cracked | 🟨 | Accepted: perks are cosmetic, so signature verification is sufficient; no secret ships in the client beyond a public key; codes can carry an expiry/nonce. Codes are issued only to **active, paying** patrons. |
| Beta builds are a supporter perk, but the repo is public | 🟨 | Soft-gated: the in-app Beta channel and the website's beta download are supporter-only, but a GitHub pre-release is still directly downloadable. Accepted (a perk, not paid content); serve betas from an authenticated endpoint if hard-gating is ever needed. |

## 10. Window / UX (custom chrome, DPI, responsiveness, a11y)
| Risk | Sev | Mitigation |
|---|---|---|
| Custom title bar breaks min/max/snap/drag | 🟧 | `WindowChrome` with correct caption + resize-border hit-testing; system commands wired explicitly. |
| Blurry UI on high-DPI / multi-monitor | 🟨 | Per-Monitor-V2 DPI awareness; vector icons; layout in DIPs. |
| Small window clips content | 🟨 | Minimum window size + fluid grids that reflow (the design already wraps panels). |
| Keyboard / screen-reader users | 🟨 | Focus order, access keys, and `AutomationProperties` on interactive elements. |

## 11. Cross-cutting (persistence, networking, concurrency, security)
| Risk | Sev | Mitigation |
|---|---|---|
| JSON store corruption on crash/power-loss | 🟥 | All stores use atomic temp-then-rename writes; a corrupt file is quarantined and defaults restored. |
| Tampered / MITM download | 🟥 | HTTPS only; every download verified against the **sha512** supplied by the source. |
| Zip-slip path traversal from a malicious archive | 🟥 | Archives are only **read in memory** for metadata; entry names are sanitized; nothing is extracted to disk. |
| Unbounded concurrency exhausts sockets/CPU | 🟨 | Download concurrency is semaphore-bounded from settings; `HttpClient` via `IHttpClientFactory`. |
| Silent crash | 🟧 | Global exception handlers log to `%AppData%/Lodestone/logs` and show a toast; the app never dies without a trace. |
