# Quickstart: Continuous Integration

1. Push the branch and open a pull request: all six checks ([contracts/ci-checks.md](contracts/ci-checks.md))
   appear and pass (SC-001).
2. Push a second commit quickly: the first run is cancelled (FR-007).
3. Compare the first (cold) and a later (warm) run time: warm is faster; the slowest job finishes
   within 15 minutes (SC-002).
4. Detection (SC-003), each on a throwaway commit, reverted afterwards:
   - break one backend test assertion → only `backend` fails, naming the test;
   - change an example in `contracts/openapi.yaml` without regenerating → only `contract` fails,
     listing client files and the command;
   - break a `FROM` line in the Dockerfile → only `image` fails;
   - break indentation in `compose.yaml` → only `environment` fails.
5. Review `.github/workflows/ci.yml`: `permissions: contents: read`, no `secrets.`, every `uses:` is a
   40-character SHA (SC-004).
6. Owner applies the README's branch protection steps; a PR with a failing check cannot be merged
   (SC-005).
