# Contributing to Diva AI

Thanks for your interest in contributing!

## Reporting Bugs

Open a [bug report](https://github.com/adpmca/diva-ai/issues/new?template=bug_report.yml) with:
- Steps to reproduce
- Expected vs actual behaviour
- Environment (OS, .NET version, Docker version)
- Relevant logs

## Proposing Features

Open a [feature request](https://github.com/adpmca/diva-ai/issues/new?template=feature_request.yml) describing the problem and your proposed solution before writing code.

## Dev Setup

```bash
# API
cp src/Diva.Host/appsettings.Development.example.json src/Diva.Host/appsettings.Development.json
dotnet run --project src/Diva.Host

# Admin portal
cd admin-portal && npm install && npm run dev
```

## Coding Standards

- **C#:** XML doc comments on all public API surface. Async methods end in `Async`. Namespaces match folder path (`Diva.Infrastructure.Auth`). No `var` when the type isn't obvious.
- **TypeScript:** Named exports. Co-locate types with components. No `any`.
- **Tests required** for new behaviour. Use real SQLite — no database mocking. See [docs/testing.md](docs/testing.md).
- **No secrets** in committed files — use `.env` and `appsettings.Development.json` (both gitignored).

## PR Process

1. Fork → feature branch → PR against `main`
2. One approval required + `ci.yml` must pass
3. Add a changelog entry in `docs/changelog.md`
4. Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `chore:`, `test:`, `refactor:`

## Commit Message Format

```
feat: short imperative description

Optional longer explanation of why, not what.
```
