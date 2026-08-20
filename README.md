# xtce-workshop

A GUI and CLI for loading, creating, editing, validating, and visualizing XML
files conforming to the [XTCE](https://www.omg.org/spec/XTCE) v1.2
specification. See the section headings below for scope,
requirements, and the architecture decisions made along the way.

**What it does today:**

- **Lossless editing** — load a real mission file, edit through forms (never
  raw XML), save: everything the editor doesn't model (headers, encodings,
  alarms, algorithms, …) is preserved verbatim, and writer output validates
  against the actual XTCE 1.2 XSD.
- **Editable constructs** — SpaceSystem hierarchies, all ten parameter type
  kinds (including enumeration lists, array dimensions, aggregate members),
  parameters, sequence containers with entry-list/packet-layout editing and
  inheritance, messages, and meta-commands — in a searchable master-detail
  UI.
- **Validation** — 21 semantic rules distilled from the XSD's normative
  documentation and CCSDS 660.1-G-2 (dangling references, inheritance
  cycles, type/value mismatches, verifier duplication, …), each
  adversarially verified end-to-end. Findings surface live in the editor as
  you type, and via `xtce-workshop validate` on the command line.
- **Visualization** — a computed static bit layout (packet visualizer) for
  any sequence container, and an in-app XTCE reference sheet showing the
  spec's own documentation for the selected construct.

## Quick start

The whole stack (backend + frontend) is containerized and runs with one
command:

```
docker compose up --build
```

- App (UI + API): http://localhost:4200 (health check: http://localhost:4200/api/health)

A single container runs Kestrel serving both the Angular frontend and the
`/api/*` endpoints — opening http://localhost:4200 in a browser is enough.

## Repository layout

```
src/
  Xtce.Workshop.Model/       Domain model + XTCE XML reader/writer (no API/UI concerns)
  Xtce.Workshop.Validation/  Validation rules + name-reference resolver
  Xtce.Workshop.Api/         .NET 8 backend (ASP.NET Core, controllers; also serves the built frontend)
  Xtce.Workshop.Cli/         `xtce-workshop validate` command-line tool
  Xtce.SpecTools/            Standalone CLI for XTCE spec rule-extraction research —
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

## Validating from the command line

`Xtce.Workshop.Cli` exposes the same validator the web UI uses. Containerized
(no host toolchain needed):

```
docker run --rm -v "$(pwd)":/workspace -w /workspace \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run --project src/Xtce.Workshop.Cli -- validate samples/telemetry-1.2.xml
```

- `validate <file.xml>` prints one line per finding
  (`severity RuleId @ Location: message`).
- `--json` emits the same `{ "validationIssues": [...] }` shape as the API.
- Exit codes: `0` no findings, `1` findings reported, `2` unusable input
  (missing file / malformed XML / usage error).

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
`node:20-bookworm@sha256:...`) — pinned by digest, not floating tag, so local
builds and CI run identical toolchains.

## License

xtce-workshop is licensed under the [MIT License](LICENSE).

Two bundled third-party files are NOT covered by that license and keep their
own terms: `reference/*/SpaceSystem.xsd` (the OMG XTCE XML Schema, used under
the XTCE specification's implementation grant — XTCE is published under OMG's
royalty-free RF-Limited IPR mode) and `reference/1.2/xml.xsd` (W3C, under the
W3C Software License). The specification documents themselves are not
redistributed here — see `reference/README.md`.
