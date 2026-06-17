# Contributing to Lodestone

Thanks for your interest in improving Lodestone! This guide covers how to build the project, the
conventions we follow, and how changes get merged. By participating you agree to abide by our
[Code of Conduct](CODE_OF_CONDUCT.md).

## Getting set up

You need the **.NET 10 SDK** (pinned in [`global.json`](global.json)) on Windows.

```powershell
dotnet restore
dotnet build
dotnet test                                   # full unit-test suite (xUnit + Shouldly + NSubstitute)
dotnet run --project src/Lodestone.App        # launch the app
```

A headless smoke check renders every screen and exits non-zero on error:

```powershell
$env:LODESTONE_SMOKE = "1"; dotnet run --project src/Lodestone.App
```

## Architecture

Lodestone uses Clean / Onion layering with MVVM at the edge — dependencies always point inward.
Before making non-trivial changes, skim **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

```
Lodestone.Domain          pure entities, value objects, rules — no dependencies
Lodestone.Application     ports (interfaces) + use-cases + the compatibility engine
Lodestone.Infrastructure  adapters: Modrinth API, archive readers, file system, settings, updater
Lodestone.App  (WPF)      views + viewmodels + DI composition root
Lodestone.Cli             headless surface
```

Keep new code in the right layer: domain rules never depend on infrastructure, and the UI talks to
the core only through the interfaces in `Lodestone.Application/Abstractions`.

## Coding conventions

- **Follow the surrounding style.** Formatting is enforced by [`.editorconfig`](.editorconfig);
  run `dotnet format` if in doubt.
- **Document public types.** Every public/internal type gets an XML `<summary>`; document non-obvious
  members and explain tricky logic with a short inline comment — match the existing density.
- **Tests first** for behaviour changes. Put domain/application tests in the matching `tests/` project.
- **Result over exceptions** for expected failures (see `Lodestone.Domain.Common.Result`).
- Nullable reference types are enabled solution-wide; keep the build warning-clean.

## Branching policy

- `main` is the default branch and must always build and pass tests. Don't commit to it directly —
  open a pull request.
- Work on a short-lived branch named `<type>/<short-slug>`, where `<type>` matches the commit types
  below. Examples:
  - `feat/curseforge-source`
  - `fix/onboarding-folder-validation`
  - `docs/architecture-update`
  - `chore/bump-dependencies`
- Release preparation, when needed, lives on `release/X.Y.Z`.
- Rebase or merge `main` into your branch before opening the PR; delete the branch after merge.

## Commit messages

We use [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): summary`.

Common types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `build`, `ci`, `perf`.

```
feat(browse): add CurseForge as a selectable mod source
fix(library): repair a stale stored version selection
docs: document the compatibility rule engine
```

Keep the subject in the imperative mood and under ~72 characters; use the body to explain *why*.

> The public **website changelog is generated from these commit subjects** — grouped into
> NEW / IMPROVED / FIXED by type — so a clear subject line becomes a clear changelog entry.
> Housekeeping types (`docs`, `ci`, `chore`, `build`, `test`) and non-app scopes (e.g. `website`,
> `design`) are filtered out of it.

## Pull requests

1. Make sure `dotnet build` and `dotnet test` are green locally.
2. Open the PR against `main` and fill in the template.
3. CI must pass before review. Keep PRs focused — one logical change per PR is easiest to review.

## Releases

Releases are cut by the maintainer by pushing a `vX.Y.Z` tag, which triggers the
[release workflow](.github/workflows/release.yml) (Velopack packages the installer and publishes it
to GitHub Releases). Don't push version tags in PRs. See **[docs/HANDOFF.md](docs/HANDOFF.md)** for
the full maintainer runbook.
