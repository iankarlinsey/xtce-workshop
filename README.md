# xtce-workshop

A GUI for loading, creating, editing, and (eventually) validating XML files
conforming to the [XTCE](https://www.omg.org/spec/XTCE) v1.2 specification.
See [`summary.md`](summary.md) for full project scope, requirements, and the
architecture decisions made along the way.

## Quick start

The whole stack (backend + frontend) is containerized and runs with one
command:

```
docker compose up --build
```

- Frontend: http://localhost:4200
- Backend directly: http://localhost:5299/api/health

The frontend's nginx proxies `/api/*` requests to the backend container, so
just opening http://localhost:4200 in a browser is enough — no separate API
URL configuration needed.

## Repository layout

```
src/
  Xtce.Workshop.Model/   Domain model + XTCE XML reader/writer (no API/UI concerns)
  Xtce.Workshop.Api/     .NET 8 backend (ASP.NET Core minimal API)
  Xtce.SpecTools/        Standalone CLI for XTCE spec rule-extraction research —
                          unrelated to the app itself, see its own project for context
tests/                   One test project per src/ project, same name + .Tests
web/                     Angular 20 frontend
reference/                Formal XTCE spec documents (OMG + CCSDS), by version
research/                 Derived research notes (e.g. OSS XTCE test-idea harvest)
samples/                  Small hand-authored XTCE fixture files used by tests
```

## Running without Docker

Requires the .NET 8 SDK (pinned via `global.json`) and Node 20+.

**Backend:**
```
dotnet run --project src/Xtce.Workshop.Api
```
Runs on http://localhost:5299 by default (see `src/Xtce.Workshop.Api/Properties/launchSettings.json`).

**Frontend:**
```
cd web
npm ci
npm start
```
`npm start` runs `ng serve` with `proxy.conf.json`, which forwards `/api/*`
to `http://localhost:5299` — so the backend needs to already be running.
Opens on http://localhost:4200.

## Tests

**Backend** (from the repo root):
```
dotnet test
```

**Frontend** (from `web/`) — needs a headless Chrome/Chromium available;
CI runs this inside a container that installs it, see
`.gitea/workflows/ci.yml` for the exact recipe:
```
cd web
npm ci
npx ng test --watch=false
```

## CI

`.gitea/workflows/ci.yml` runs on every push/PR to `master`: builds and
tests both the backend and frontend, each in a digest-pinned container
matching what's used for local Docker builds (`mcr.microsoft.com/dotnet/sdk:8.0`,
`node:20-bookworm@sha256:...`) — see `summary.md`'s Architecture Decisions
for why pinning by digest, not a floating tag, matters here.
