#Requires -Version 5.1
<#
.SYNOPSIS
    Brings up the AWSRedrive integration test stack.

.DESCRIPTION
    Builds and starts Floci (SQS), Kafka, the recording sink and AWSRedrive
    itself, then blocks until every one of them is actually serving. Run this
    before running the integration tests from Visual Studio or the CLI.

.PARAMETER NoBuild
    Skip the image build and start whatever is already built.

.PARAMETER TimeoutSeconds
    How long to wait for any single service to become ready.

.EXAMPLE
    ./start.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 180
)

# Deliberately NOT 'Stop'. Native executables write progress and warnings to
# stderr - `docker info` emits "WARNING: No blkio throttle.read_bps_device
# support", `docker compose up` writes its whole progress display there. Under
# 'Stop', Windows PowerShell wraps each of those lines in a NativeCommandError
# and kills the script. Exit codes are checked explicitly instead, and Fail is
# used for anything genuinely fatal.
$ErrorActionPreference = 'Continue'
Set-Location -Path $PSScriptRoot

function Fail {
    param([string]$Message)

    Write-Host ""
    Write-Host $Message -ForegroundColor Red
    exit 1
}

<#
.SYNOPSIS
    Runs docker, swallowing its output, and returns whether it succeeded.
#>
function Invoke-DockerQuiet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$DockerArgs)

    $null = & docker @DockerArgs 2>&1
    return ($LASTEXITCODE -eq 0)
}

# --------------------------------------------------------------------------
# Settings, read from .env so the ports live in exactly one place
# --------------------------------------------------------------------------
function Get-DotEnvValue {
    param([string]$Name, [string]$Default)

    $envFile = Join-Path $PSScriptRoot '.env'
    if (Test-Path $envFile) {
        foreach ($line in Get-Content $envFile) {
            if ($line -match "^\s*$([regex]::Escape($Name))\s*=\s*(.+?)\s*$") {
                return $Matches[1]
            }
        }
    }
    return $Default
}

$flociPort = Get-DotEnvValue -Name 'FLOCI_PORT'             -Default '4566'
$sinkPort = Get-DotEnvValue -Name 'SINK_HTTP_PORT'          -Default '8080'
$sinkTlsPort = Get-DotEnvValue -Name 'SINK_HTTPS_PORT'      -Default '8443'
$kafkaPort = Get-DotEnvValue -Name 'KAFKA_PORT'             -Default '29092'
$dashPort = Get-DotEnvValue -Name 'REDRIVE_DASHBOARD_PORT'  -Default '5000'
$dozzlePort = Get-DotEnvValue -Name 'DOZZLE_PORT'           -Default '9999'

# --------------------------------------------------------------------------
# Preconditions
# --------------------------------------------------------------------------
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Fail "docker was not found on PATH. Install Docker Desktop and try again."
}

if (-not (Invoke-DockerQuiet info)) {
    Fail "The Docker daemon is not responding. Start Docker Desktop and try again."
}

$env:COMPOSE_PROJECT_NAME = 'awsredrive-it'

# --------------------------------------------------------------------------
# Readiness helpers
# --------------------------------------------------------------------------
function Wait-ForHttp {
    param([string]$Name, [string]$Url, [int]$Timeout)

    Write-Host -NoNewline "  waiting for $Name ... "
    $deadline = (Get-Date).AddSeconds($Timeout)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Host "ok" -ForegroundColor Green
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 700
        }
    }
    Write-Host "TIMEOUT" -ForegroundColor Red
    return $false
}

function Wait-ForInitContainer {
    param([string]$Service, [int]$Timeout)

    Write-Host -NoNewline "  waiting for $Service to complete ... "
    $deadline = (Get-Date).AddSeconds($Timeout)
    while ((Get-Date) -lt $deadline) {
        $id = (& docker compose ps -a -q $Service 2>$null | Select-Object -First 1)
        if ($id) {
            $state = (& docker inspect -f '{{.State.Status}}:{{.State.ExitCode}}' $id 2>$null | Select-Object -First 1)
            if ($state -eq 'exited:0') {
                Write-Host "ok" -ForegroundColor Green
                return $true
            }
            if ($state -like 'exited:*') {
                Write-Host "FAILED ($state)" -ForegroundColor Red
                & docker compose logs $Service
                return $false
            }
        }
        Start-Sleep -Milliseconds 700
    }
    Write-Host "TIMEOUT" -ForegroundColor Red
    & docker compose logs $Service
    return $false
}

# --------------------------------------------------------------------------
# Up
# --------------------------------------------------------------------------
Write-Host "Starting the AWSRedrive integration stack..." -ForegroundColor Cyan

$upArgs = @('compose', 'up', '-d')
if (-not $NoBuild) { $upArgs += '--build' }

& docker @upArgs
if ($LASTEXITCODE -ne 0) {
    Fail "docker compose up failed."
}

Write-Host ""

if (-not (Wait-ForInitContainer -Service 'floci-init' -Timeout $TimeoutSeconds)) {
    & docker compose logs floci
    Fail "Queue seeding did not complete. Floci may not have started."
}

if (-not (Wait-ForInitContainer -Service 'kafka-init' -Timeout $TimeoutSeconds)) {
    & docker compose logs kafka
    Fail "Kafka topic creation did not complete."
}

if (-not (Wait-ForHttp -Name 'sink' -Url "http://localhost:$sinkPort/health" -Timeout $TimeoutSeconds)) {
    & docker compose logs sink
    Fail "The sink service did not become healthy."
}

if (-not (Wait-ForHttp -Name 'redrive' -Url "http://localhost:$dashPort/health" -Timeout $TimeoutSeconds)) {
    & docker compose logs redrive
    Fail "Redrive did not become healthy."
}

# Dozzle is a convenience, not a dependency - the tests never touch it, so a
# slow or unhappy log viewer must not stop a run.
if (-not (Wait-ForHttp -Name 'dozzle' -Url "http://localhost:$dozzlePort/healthcheck" -Timeout 20)) {
    Write-Host "  (log viewer did not answer - the stack is still usable)" -ForegroundColor Yellow
}

# --------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------
Write-Host ""
Write-Host "Stack is up." -ForegroundColor Green
Write-Host ""
Write-Host "  Floci (SQS)        http://localhost:$flociPort"
Write-Host "  Kafka (external)   localhost:$kafkaPort"
Write-Host "  Sink (HTTP)        http://localhost:$sinkPort"
Write-Host "  Sink (HTTPS)       https://localhost:$sinkTlsPort"
Write-Host "  Redrive dashboard  http://localhost:$dashPort"
Write-Host "  Logs (Dozzle)      http://localhost:$dozzlePort" -ForegroundColor Cyan
Write-Host ""
Write-Host "Run the tests from Visual Studio, or:" -ForegroundColor Cyan
Write-Host "  dotnet test --project Tests/AWSRedrive.Test.Integration"
Write-Host ""
Write-Host "Tear the stack down with ./stop.ps1" -ForegroundColor Cyan
