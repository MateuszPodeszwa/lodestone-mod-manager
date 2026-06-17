# Architecture

Lodestone is built as a **Clean / Onion architecture** with **MVVM** at the presentation edge.
The guiding rule: *dependencies point inward*. The domain knows nothing about WPF, HTTP, or the
file system; those are details that plug into interfaces (ports) defined further in.

```
┌──────────────────────────────────────────────────────────────────────┐
│  Lodestone.App (WPF)         Views · ViewModels · DI composition root  │
│  Lodestone.Cli               headless command surface                  │
└───────────────▲───────────────────────────────────▲──────────────────┘
                │ implements ports                   │ uses use-cases
┌───────────────┴───────────────────────────────────┴──────────────────┐
│  Lodestone.Infrastructure    Modrinth client · ArchiveMetadataReader   │
│                              FileSystemContentInstaller · Settings      │
│                              GameLocator · Downloader · Updater · Codes  │
└───────────────▲───────────────────────────────────────────────────────┘
                │ implements ports / returns domain types
┌───────────────┴───────────────────────────────────────────────────────┐
│  Lodestone.Application       Ports (interfaces) · Use-cases             │
│                              CompatibilityService (rule pipeline)        │
└───────────────▲───────────────────────────────────────────────────────┘
                │ depends only on
┌───────────────┴───────────────────────────────────────────────────────┐
│  Lodestone.Domain            Entities · Value Objects · Result · Rules   │
└───────────────────────────────────────────────────────────────────────┘
```

## Why these boundaries

- **Testability** — the entire decision-making core (install pipeline, compatibility detection,
  update resolution, settings) is exercised by unit tests with no UI, network or disk.
- **Replaceability** — Modrinth vs CurseForge, JSON-on-disk vs a future database, WPF vs a future
  macOS UI: each is a swap behind an interface, not a rewrite.
- **Honoring constraints** — "no background processes" and "changes persist until the user acts"
  are properties of the *Application* layer (no timers, explicit refresh), independent of the UI.

## Patterns used (and where)

| Pattern | Where | Why |
|---|---|---|
| **MVVM** | `App/ViewModels/*`, `App/Views/*` | Separates view state from rendering; testable VMs. |
| **Dependency Inversion** | every `I*` port in `Application` | UI/Infra depend on abstractions. |
| **Repository** | `IInstalledContentRepository` | Persistence of the library behind an interface. |
| **Strategy** | `IModSource` (Modrinth/CurseForge), `IArchiveMetadataReader` (fabric/forge/quilt) | Interchangeable algorithms chosen at runtime. |
| **Factory** | `IModSourceRegistry`, installer factory by `ContentType` | Centralised creation/selection. |
| **Chain of Responsibility** | `CompatibilityService` + `ICompatibilityRule` pipeline | Each rule independently flags one class of issue. |
| **Specification** | `LibraryQuery` filters (type/version/search) | Composable, testable query predicates. |
| **Decorator** | `CachingModSource`, `RetryHandler` around `IModSource`/`HttpClient` | Cross-cutting concerns without touching the core client. |
| **Adapter** | Modrinth DTO → domain mappers | Keeps external API shapes out of the domain. |
| **Result / Railway** | `Result`, `Result<T>` | Expected failures are values, not exceptions. |
| **Options** | `LodestoneSettings` + `ISettingsStore` | Validated, versioned configuration. |
| **Observer** | settings-changed event, `IProgress<InstallProgress>` | Push updates to the UI. |
| **Null Object** | `Loader.None` | Resource packs/shaders have "no loader" without nulls. |
| **Template Method** | `ContentInstallerBase` | Shared install skeleton, per-type specifics. |
| **Command** | `RelayCommand` (CommunityToolkit) | UI actions as first-class objects. |
| **Mediator (light)** | `IMessageBus` | Toasts/cross-VM signals without hard references. |
| **Builder** | `CompatibilityReportBuilder` | Assemble a per-item issue set incrementally. |

## Key flows

### Drag-and-drop install
`MainWindow.Drop` → `InstallLocalFileUseCase`:
1. `IArchiveMetadataReader` inspects the archive **in memory** (no extraction) → mod id, loader,
   declared dependencies, content type.
2. `IContentInstaller` copies the file into `.minecraft/<mods|resourcepacks|shaderpacks>` for the
   **active game version**, soft-handling duplicates.
3. `IInstalledContentRepository` records it; `CompatibilityService` re-scans; the UI shows any
   issue symbol immediately.

### Browse → install (Modrinth)
`BrowseViewModel.Search` → `IModSource.SearchAsync` (via `CachingModSource` + retry) →
`InstallFromCatalogUseCase` resolves the best `ProjectVersion` for the active game version + loader,
`IDownloader` fetches it (bounded by the *concurrent downloads* setting) and verifies **sha512**.

### Compatibility detection
On every refresh, `CompatibilityService` builds lookup indexes once, then runs the rule pipeline
over the installed set for the active version. Each `ICompatibilityRule` yields zero or more
`CompatibilityIssue`s (severity + kind + symbol + message + related project). The UI binds the
highest-severity symbol next to each item and lists all issues in the tooltip.

### Updates
`RefreshUpdatesUseCase` runs **on app start and on manual refresh only**. For each enabled item it
asks the source for the latest version that is compatible with the active game version + loader.
"Update all" and opt-in auto-update both reuse this resolver; the previous file is moved to a trash
folder so a bad update is recoverable.

## Deployment & auto-update

- Packaging and self-update use **Velopack**. The `release.yml` workflow builds on a `v*` tag,
  produces the installer + delta packages, and publishes a GitHub Release that acts as the update
  feed for installed clients.
- **Code signing** is optional and documented as a hand-off step: provide a certificate via the
  `VPK_SIGN_*` secrets and the release workflow signs the artifacts. Unsigned builds still work but
  may trigger SmartScreen on first run.
- **Early-access (beta) channel.** A pre-release tag (`vX.Y.Z-beta.N`) is published as a GitHub
  *pre-release* (`release.yml` adds `--pre`); `VelopackAppUpdater` includes pre-releases only on the
  supporter-gated Beta channel (`GithubSource(prerelease: true)`), so betas reach supporters without
  affecting stable clients. See [SUPPORTERS.md](SUPPORTERS.md#early-access-beta-builds).

## Configuration & data locations

| What | Path |
|---|---|
| Settings | `%AppData%/Lodestone/settings.json` (atomic write, schema-versioned) |
| Library index | `%AppData%/Lodestone/library.json` |
| Entitlements (supporter) | `%AppData%/Lodestone/entitlements.json` |
| HTTP cache | `%LocalAppData%/Lodestone/cache/` |
| Logs | `%AppData%/Lodestone/logs/` |
| Soft-delete trash | `%AppData%/Lodestone/trash/` |

## Threading & performance

All I/O is `async`; the UI thread only does rendering and `IProgress` callbacks. Downloads are
bounded by a `SemaphoreSlim` sized from settings. Lists are virtualized. Compatibility scans are
O(n) over the library using pre-built indexes.
