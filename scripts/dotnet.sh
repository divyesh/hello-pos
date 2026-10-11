#!/usr/bin/env bash
# Runs `dotnet` inside the official .NET 10 SDK container, so no local SDK is needed.
#
# Usage: scripts/dotnet.sh [--port <n>]... <dotnet arguments>
#
#   scripts/dotnet.sh build backend/HelloPos.slnx
#   scripts/dotnet.sh --port 5080 run --project backend/src/HelloPos.Api
#
#   --port <n>        Publish container port n on the host (repeatable). No ports are published
#                     by default, so builds and tests can run while the API or web app is up.
#   HELLOPOS_PORTS    Comma-separated ports to publish, in addition to --port.
#   CONTAINER_ENGINE  podman (default) or docker.
#
# These host environment variables are passed into the container when set:
# Seq__ServerUrl, Seq__ApiKey, ASPNETCORE_ENVIRONMENT.
# The container runs as the calling user, so files it creates are not owned by root.
set -euo pipefail

image="mcr.microsoft.com/dotnet/sdk:10.0"
engine="${CONTAINER_ENGINE:-podman}"

ports=()
while [[ $# -gt 0 && "$1" == "--port" ]]; do
  [[ $# -ge 2 ]] || { echo "--port needs a port number." >&2; exit 2; }
  ports+=("$2")
  shift 2
done
if [[ -n "${HELLOPOS_PORTS:-}" ]]; then
  IFS=',' read -ra extra <<< "$HELLOPOS_PORTS"
  for p in "${extra[@]}"; do [[ -n "${p// /}" ]] && ports+=("${p// /}"); done
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work_dir="/repo"
case "$PWD/" in
  "$repo_root/"*) rel="${PWD#"$repo_root"}"; work_dir="/repo${rel}" ;;
esac

run_args=(run --rm -i
  -v "$repo_root:/repo"
  -v hellopos-nuget:/nuget
  -w "$work_dir"
  --user "$(id -u):$(id -g)"
  -e HOME=/tmp/home
  -e NUGET_PACKAGES=/nuget
  -e DOTNET_CLI_HOME=/nuget/.cli-home
  -e DOTNET_NOLOGO=1
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1
  --add-host host.docker.internal:host-gateway)
[[ -t 0 && -t 1 ]] && run_args+=(-t)
if [[ ${#ports[@]} -gt 0 ]]; then
  for p in $(printf '%s\n' "${ports[@]}" | sort -u); do run_args+=(-p "$p:$p"); done
fi
for name in Seq__ServerUrl Seq__ApiKey ASPNETCORE_ENVIRONMENT; do
  [[ -n "${!name:-}" ]] && run_args+=(-e "$name=${!name}")
done

# Tests start throwaway containers (Testcontainers, e.g. a real PostgreSQL), so `test` runs get the
# container engine's socket. Only for `test`: the socket gives full control of the engine.
if [[ "${1:-}" == "test" ]]; then
  if [[ -n "${HELLOPOS_ENGINE_SOCKET:-}" ]]; then socket="$HELLOPOS_ENGINE_SOCKET"
  elif [[ "$engine" == "docker" ]]; then socket=/var/run/docker.sock
  else socket="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/podman/podman.sock"; [[ -S "$socket" ]] || socket=/run/podman/podman.sock
  fi
  run_args+=(-v "$socket:/var/run/docker.sock"
    -e DOCKER_HOST=unix:///var/run/docker.sock
    -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal
    -e TESTCONTAINERS_RYUK_DISABLED=true)
  # The container runs as the calling user, who needs the socket's group to use it.
  if [[ -S "$socket" ]]; then run_args+=(--group-add "$(stat -c %g "$socket" 2>/dev/null || stat -f %g "$socket")"); fi
fi

exec "$engine" "${run_args[@]}" "$image" dotnet "$@"
