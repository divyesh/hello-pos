# Research: Continuous Integration

Versions resolved 2026-10-11. Every action is referenced by its full commit SHA with the tag in a
comment (FR-010).

| Action | Tag | Commit |
|--------|-----|--------|
| `actions/checkout` | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| `actions/setup-dotnet` | v6.0.0 | `a98b56852c35b8e3190ac28c8c2271da59106c68` |
| `actions/setup-java` | v6.0.1 | `de7274f081f381c8f8158605e0321c36c376e2e6` |
| `actions/setup-node` | v7.1.0 | `949feb2413d6458794dcd2491c4babbbce0c15c1` |
| `actions/cache` | v6.1.0 | `55cc8345863c7cc4c66a329aec7e433d2d1c52a9` |
| `actions/upload-artifact` | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` |
| `gradle/actions` (setup-gradle) | v6.4.0 | `3f5f9adaf7d9fecd50b5935e54106014257a94e6` |

Node.js 24 LTS for the contract linter; `@redocly/cli` 2.62.1.

## R1. One workflow, six jobs

- **Decision**: `.github/workflows/ci.yml`, triggers `pull_request` (to `main`) and `push` (to `main`),
  no path filters (FR-001). Jobs `backend`, `web`, `android`, `contract`, `image`, `environment` run in
  parallel on `ubuntu-24.04`; each job name is the check name the owner makes required.
  `concurrency: ci-${{ github.ref }}` with `cancel-in-progress: true` (FR-007).
  Top-level `permissions: contents: read`; no `secrets` references; `pull_request` (not
  `pull_request_target`), so fork PRs run with a read-only token and no secrets (FR-009).
- **Rationale**: parallel jobs keep wall time near the slowest job (SC-002) and give one check per area
  (FR-002).
- **Alternatives**: one job per workflow (more files, same result); path filters (would let a change
  skip checks; spec forbids).

## R2. Tools on the runner, not the developer container

- **Decision**: CI installs the pinned toolchains directly: `setup-dotnet` with `global-json-file`
  (honours the SDK pin, FR-005), `setup-java` Temurin 21, Gradle via the committed wrapper with
  `gradle/actions/setup-gradle` (also validates the wrapper JAR checksum). The runner's Docker serves
  Testcontainers natively; no socket mounting or overrides are needed.
- **Rationale**: the container wrapper exists for local Windows/Podman machines; on Linux runners the
  native tools are faster and simpler. Same versions either way.

## R3. Caching

- **Decision**: NuGet packages cached with `actions/cache` keyed on `Directory.Packages.props`,
  `**/*.csproj`, `global.json` and `.config/dotnet-tools.json` (FR-008). Gradle caching by
  `setup-gradle` (keyed on build files and the version catalog). npm packages are not cached (one small
  `npx` download).
- **Alternatives**: `setup-dotnet`'s built-in cache (requires NuGet lock files the repo does not use).

## R4. Test results

- **Decision**: .NET tests run with `--report-xunit-trx --results-directory TestResults` (xUnit v3 on
  Microsoft.Testing.Platform); failing tests are printed in the log and the TRX files are uploaded as
  an artifact on every run, pass or fail. Android uploads `app/build/reports/tests` and
  `app/build/test-results`. No automatic retries (FR-011).

## R5. Contract check

- **Validity**: `npx @redocly/cli@2.62.1 lint contracts/openapi.yaml` with `redocly.yaml` enabling only
  structural rules (`struct`, `no-unresolved-refs`): it fails on an invalid description but not on
  style preferences the project has not adopted.
- **Drift**: `dotnet tool restore`, run the same `kiota generate` command as `web/.../ApiClient/README.md`,
  then `git status --porcelain web/src/HelloPos.Web/ApiClient`; any output fails the job, printing the
  diff and the regeneration command (FR-004).
- **Line endings**: `kiota-lock.json` records a hash of the contract file. Windows checkouts used CRLF,
  Linux LF, so hashes would differ. `.gitattributes` pins `contracts/*.yaml` and `contracts/*.json` to
  LF and the client is regenerated once from the LF file, so local and CI hashes agree.

## R6. Image and environment checks

- **Image**: `docker build -f backend/src/HelloPos.Api/Dockerfile .` with no push and no registry login
  (FR-006).
- **Environment**: copy `.env.example` to `.env`, then
  `docker compose --profile api --profile tools config --quiet` (validates syntax, interpolation and
  references for every service).

## R7. Required checks

- **Decision**: documented in the README: GitHub → Settings → Rules (or Branches) → protect `main`:
  require a pull request, require status checks `backend`, `web`, `android`, `contract`, `image`,
  `environment`, require branches to be up to date. Applied by the owner (needs admin rights).
