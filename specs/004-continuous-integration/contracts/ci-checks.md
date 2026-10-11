# CI Checks (contract with the repository owner)

These job names are the status checks branch protection refers to. Renaming one is a breaking change:
update the required checks in GitHub at the same time.

| Check | Proves | Reproduce locally |
|-------|--------|-------------------|
| `backend` | Backend builds; all backend tests pass, including real-PostgreSQL tests | `scripts/dotnet.ps1 test --solution backend/HelloPos.slnx` |
| `web` | Web back office builds; all web tests pass | `scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx` |
| `android` | Android app builds; all unit tests pass | `cd android; ./gradlew assembleDebug testDebugUnitTest` |
| `contract` | `contracts/openapi.yaml` is valid; the committed web client matches it | `npx @redocly/cli@2.62.1 lint contracts/openapi.yaml`, then the Kiota command in `web/src/HelloPos.Web/ApiClient/README.md` and `git status` |
| `image` | The API container image builds | `podman build -f backend/src/HelloPos.Api/Dockerfile .` |
| `environment` | `compose.yaml` is valid for every profile | `podman compose --env-file .env.example --profile api --profile tools config --quiet` |

Triggers: every pull request to `main` and every push to `main`. Permissions: read-only. Secrets: none.
