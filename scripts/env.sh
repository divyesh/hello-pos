#!/usr/bin/env bash
# Manages the Hello POS local environment (feature 002): PostgreSQL, mock API, Seq, optional API.
#
# Usage: scripts/env.sh <command> [options]
#   up [--api]      Start postgres, mock-api and seq (and build + start the API with --api);
#                   waits until every service is healthy, then prints addresses.
#   down            Stop and remove the containers; data is kept.
#   reset [--yes]   Stop everything and DELETE this environment's data (asks unless --yes).
#   status          Show services and their health.
#   logs [service]  Follow logs (all services, or one).
#
# CONTAINER_ENGINE  podman (default) or docker.
# Settings come from .env (created from .env.example when missing). Only compose project
# "hellopos" is touched.
set -euo pipefail

engine="${CONTAINER_ENGINE:-podman}"
export PODMAN_COMPOSE_WARNING_LOGS=false
export COMPOSE_BAKE=false   # classic builds; Bake needs buildkit, which podman lacks
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"
# A changed contract changes mock-api's configuration, so compose re-creates it (Prism reloads).
HELLOPOS_CONTRACT_HASH="$( (sha256sum "$repo_root/contracts/openapi.yaml" 2>/dev/null || shasum -a 256 "$repo_root/contracts/openapi.yaml") | cut -d' ' -f1)"
export HELLOPOS_CONTRACT_HASH

command="${1:-help}"
shift || true
with_api=false; assume_yes=false; service=()
for arg in "$@"; do
  case "$arg" in
    --api) with_api=true ;;
    --yes) assume_yes=true ;;
    --*) echo "Unknown option: $arg" >&2; exit 2 ;;
    *) service+=("$arg") ;;
  esac
done

compose() {
  "$engine" compose --project-name hellopos --env-file "$env_file" -f "$repo_root/compose.yaml" "$@"
}

init_env_file() {
  if [[ ! -f "$env_file" ]]; then
    cp "$repo_root/.env.example" "$env_file"
    echo "Created .env from .env.example (development placeholders). Edit it to change ports or passwords."
  fi
}

setting() { grep -E "^\s*$1\s*=" "$env_file" | tail -1 | sed -E 's/^[^=]*=\s*//; s/\s*$//'; }

usage() { sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

case "$command" in
  up)
    init_env_file
    args=()
    $with_api && args+=(--profile api)
    args+=(up -d --wait --wait-timeout 180)
    $with_api && args+=(--build)
    if ! compose "${args[@]}"; then
      echo
      echo "Environment failed to start. Service status:" >&2
      compose ps --all --format 'table {{.Service}}\t{{.State}}\t{{.Status}}' >&2 || true
      exit 1
    fi
    bind="$(setting HELLOPOS_BIND_ADDRESS)"; [[ "$bind" == "0.0.0.0" ]] && bind=localhost
    echo
    echo "Hello POS local environment is ready:"
    echo "  Database   $bind:$(setting HELLOPOS_PG_PORT)  (database $(setting HELLOPOS_DB_NAME), app login $(setting HELLOPOS_DB_APP_USER))"
    echo "  Mock API   http://$bind:$(setting HELLOPOS_MOCK_PORT)"
    echo "  Seq        http://$bind:$(setting HELLOPOS_SEQ_PORT)"
    if $with_api; then
      url="http://$bind:$(setting HELLOPOS_API_PORT)/health"
      for _ in $(seq 1 30); do
        if health="$(curl -fsS --max-time 5 "$url" 2>/dev/null)"; then
          echo "  API        http://$bind:$(setting HELLOPOS_API_PORT)  ($health)"
          exit 0
        fi
        sleep 2
      done
      echo "  API        $url did not answer within 60 s; see: scripts/env.sh logs api" >&2
      exit 1
    fi
    ;;
  down)
    init_env_file
    compose --profile api down --remove-orphans
    ;;
  reset)
    init_env_file
    if ! $assume_yes; then
      read -r -p "This deletes ALL local Hello POS data (database and Seq). Type yes to continue: " answer
      [[ "$answer" == "yes" ]] || { echo "Reset cancelled."; exit 1; }
    fi
    compose --profile api down --volumes --remove-orphans
    echo "Local environment data deleted. Run \"scripts/env.sh up\" to start fresh."
    ;;
  status)
    init_env_file
    compose --profile api ps --all
    ;;
  logs)
    init_env_file
    compose --profile api logs -f "${service[@]}"
    ;;
  help) usage ;;
  *) usage; exit 2 ;;
esac
