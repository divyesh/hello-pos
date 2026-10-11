# CI Results: Continuous Integration

## Local validation (T012), 2026-10-11

| Check | Result |
|-------|--------|
| Workflow YAML parses; jobs `backend`, `web`, `android`, `contract`, `image`, `environment` | ✅ |
| All 18 `uses:` pinned to 40-character commit SHAs | ✅ |
| `permissions: contents: read`; no `secrets.`; `pull_request` (not `pull_request_target`); no retries | ✅ |
| `compose.yaml` valid with `.env.example` for profiles `api` and `tools` | ✅ |
| Redocly lint: real contract passes; a contract with `pathz:` fails naming line 17 | ✅ |
| Drift check: forced regeneration of the unchanged client shows no diff; a hand edit is detected (exit 1) and overwritten by regeneration | ✅ |
| TRX reporting flags work with Microsoft.Testing.Platform (`-- --report-xunit-trx --results-directory`) | ✅ |

### Issues found and fixed during planning

1. `kiota-lock.json` stores a hash of the contract; Windows (CRLF) and Linux (LF) checkouts hash
   differently. `contracts/*.yaml|json` are now pinned to LF in `.gitattributes`.
2. Kiota skips generation when the hash matches, so a hand edit to the generated client would never be
   detected. The CI step deletes `kiota-lock.json` before generating, forcing full regeneration.
3. Comparing with `git status` is fooled by working-tree line endings on Windows; the check stages the
   folder and uses `git diff --cached --exit-code`, which also catches added and deleted files.

## On GitHub (T013)

Pending: needs the branch pushed and a pull request open (quickstart.md steps 1–5).
