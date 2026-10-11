<#
.SYNOPSIS
  Manages the Hello POS local environment (feature 002): PostgreSQL, mock API, Seq, optional API.

.DESCRIPTION
  Usage: scripts/env.ps1 <command> [options]

    up [--api]      Start postgres, mock-api and seq (and build + start the API with --api);
                    waits until every service is healthy, then prints addresses.
    down            Stop and remove the containers; data is kept.
    reset [--yes]   Stop everything and DELETE this environment's data (asks unless --yes).
    status          Show services and their health.
    logs [service]  Follow logs (all services, or one).

  CONTAINER_ENGINE  podman (default) or docker.
  Settings come from .env (created from .env.example when missing). Only compose project
  "hellopos" is touched.
#>
$ErrorActionPreference = 'Stop'

$engine = if ($env:CONTAINER_ENGINE) { $env:CONTAINER_ENGINE } else { 'podman' }
$env:PODMAN_COMPOSE_WARNING_LOGS = 'false'   # hide podman's "external compose provider" banner
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envFile = Join-Path $repoRoot '.env'
$exampleFile = Join-Path $repoRoot '.env.example'

$command = if ($args.Count -gt 0) { [string]$args[0] } else { 'help' }
$options = @($args | Select-Object -Skip 1 | ForEach-Object { [string]$_ })
$withApi = $options -contains '--api'
$assumeYes = $options -contains '--yes'

function Invoke-Compose {
    param([string[]]$ComposeArgs)
    # Compose writes progress to stderr; Windows PowerShell would treat that as a terminating
    # error under 'Stop', so success is judged by the exit code only.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $engine compose --project-name hellopos --env-file $envFile -f (Join-Path $repoRoot 'compose.yaml') @ComposeArgs 2>&1 |
            ForEach-Object { Write-Host $_ }
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Get-Settings {
    $settings = @{}
    foreach ($line in Get-Content $envFile) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') { $settings[$Matches[1]] = $Matches[2] }
    }
    return $settings
}

function Initialize-EnvFile {
    if (-not (Test-Path $envFile)) {
        Copy-Item $exampleFile $envFile
        Write-Host 'Created .env from .env.example (development placeholders). Edit it to change ports or passwords.'
    }
}

function Wait-ForApi([string]$url, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod -Uri $url -TimeoutSec 5
            return $health
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    return $null
}

switch ($command) {
    'up' {
        Initialize-EnvFile
        $composeArgs = @()
        if ($withApi) { $composeArgs += @('--profile', 'api') }
        $composeArgs += @('up', '-d', '--wait', '--wait-timeout', '180')
        if ($withApi) { $composeArgs += '--build' }

        if ((Invoke-Compose $composeArgs) -ne 0) {
            Write-Host ''
            Write-Host 'Environment failed to start. Service status:' -ForegroundColor Red
            Invoke-Compose @('ps', '--all', '--format', 'table {{.Service}}\t{{.State}}\t{{.Health}}') | Out-Null
            exit 1
        }

        $s = Get-Settings
        $bind = if ($s.HELLOPOS_BIND_ADDRESS -eq '0.0.0.0') { 'localhost' } else { $s.HELLOPOS_BIND_ADDRESS }
        Write-Host ''
        Write-Host 'Hello POS local environment is ready:' -ForegroundColor Green
        Write-Host ("  Database   {0}:{1}  (database {2}, app login {3})" -f $bind, $s.HELLOPOS_PG_PORT, $s.HELLOPOS_DB_NAME, $s.HELLOPOS_DB_APP_USER)
        Write-Host ("  Mock API   http://{0}:{1}" -f $bind, $s.HELLOPOS_MOCK_PORT)
        Write-Host ("  Seq        http://{0}:{1}" -f $bind, $s.HELLOPOS_SEQ_PORT)

        if ($withApi) {
            $url = "http://{0}:{1}/health" -f $bind, $s.HELLOPOS_API_PORT
            $health = Wait-ForApi $url 60
            if ($null -eq $health) {
                Write-Host "  API        $url did not answer within 60 s; see: scripts/env.ps1 logs api" -ForegroundColor Red
                exit 1
            }
            Write-Host ("  API        http://{0}:{1}  (status: {2}, version {3})" -f $bind, $s.HELLOPOS_API_PORT, $health.status, $health.version)
        }
    }
    'down' {
        Initialize-EnvFile
        exit (Invoke-Compose @('--profile', 'api', 'down', '--remove-orphans'))
    }
    'reset' {
        Initialize-EnvFile
        if (-not $assumeYes) {
            $answer = Read-Host 'This deletes ALL local Hello POS data (database and Seq). Type yes to continue'
            if ($answer -ne 'yes') { Write-Host 'Reset cancelled.'; exit 1 }
        }
        $code = Invoke-Compose @('--profile', 'api', 'down', '--volumes', '--remove-orphans')
        if ($code -eq 0) { Write-Host 'Local environment data deleted. Run "scripts/env.ps1 up" to start fresh.' }
        exit $code
    }
    'status' {
        Initialize-EnvFile
        exit (Invoke-Compose @('--profile', 'api', 'ps', '--all'))
    }
    'logs' {
        Initialize-EnvFile
        $service = @($options | Where-Object { -not $_.StartsWith('--') })
        exit (Invoke-Compose (@('--profile', 'api', 'logs', '-f') + $service))
    }
    default {
        Get-Help $PSCommandPath -Detailed | Out-String | Write-Host
        exit $(if ($command -eq 'help') { 0 } else { 2 })
    }
}
