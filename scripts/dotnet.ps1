<#
.SYNOPSIS
  Runs `dotnet` inside the official .NET 10 SDK container, so no local SDK is needed.

.DESCRIPTION
  Usage: scripts/dotnet.ps1 [-Port <n>]... <dotnet arguments>

  Examples:
    scripts/dotnet.ps1 build backend/HelloPos.slnx
    scripts/dotnet.ps1 -Port 5080 run --project backend/src/HelloPos.Api

  -Port <n>         Publish container port n on the host (repeatable). No ports are published
                    by default, so builds and tests can run while the API or web app is up.
  HELLOPOS_PORTS    Comma-separated ports to publish, in addition to -Port.
  CONTAINER_ENGINE  podman (default) or docker.

  These host environment variables are passed into the container when set:
  Seq__ServerUrl, Seq__ApiKey, ASPNETCORE_ENVIRONMENT.
  The hostname host.docker.internal reaches the host under both Podman and Docker.
#>

# No param() block on purpose: dotnet arguments such as -p:Name=Value must not be bound to
# script parameters. Leading "-Port <n>" pairs are parsed by hand.
$ErrorActionPreference = 'Stop'

$image = 'mcr.microsoft.com/dotnet/sdk:10.0'
$engine = if ($env:CONTAINER_ENGINE) { $env:CONTAINER_ENGINE } else { 'podman' }

$ports = New-Object System.Collections.Generic.List[int]
$rest = New-Object System.Collections.Generic.List[string]
$i = 0
while ($i -lt $args.Count -and "$($args[$i])" -ieq '-Port') {
    if ($i + 1 -ge $args.Count) { throw '-Port needs a port number.' }
    $ports.Add([int]$args[$i + 1])
    $i += 2
}
for (; $i -lt $args.Count; $i++) { $rest.Add([string]$args[$i]) }
if ($env:HELLOPOS_PORTS) {
    foreach ($p in $env:HELLOPOS_PORTS.Split(',')) {
        if ($p.Trim()) { $ports.Add([int]$p.Trim()) }
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$current = (Get-Location).Path
$workDir = '/repo'
if ($current.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    $relative = $current.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    if ($relative) { $workDir = "/repo/$relative" }
}

$runArgs = @(
    'run', '--rm', '-i',
    '-v', "${repoRoot}:/repo",
    '-v', 'hellopos-nuget:/nuget',
    '-w', $workDir,
    '-e', 'NUGET_PACKAGES=/nuget',
    '-e', 'DOTNET_NOLOGO=1',
    '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
    '--add-host', 'host.docker.internal:host-gateway'
)
if (-not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected) {
    $runArgs += '-t'
}
foreach ($p in ($ports | Select-Object -Unique)) {
    $runArgs += @('-p', "${p}:${p}")
}
foreach ($name in @('Seq__ServerUrl', 'Seq__ApiKey', 'ASPNETCORE_ENVIRONMENT')) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($value) { $runArgs += @('-e', "$name=$value") }
}

& $engine @runArgs $image dotnet @rest
exit $LASTEXITCODE
